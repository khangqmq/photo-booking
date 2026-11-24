using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(tags: ["ready"]);
builder.Services.AddIdentityCore<IdentityUser>(o =>
    {
        o.Password.RequiredLength = 12;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", p => p.RequireRole("Admin"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<BookingService>();

builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()!)
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("booking", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
        }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseCors("web");
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });

var api = app.MapGroup("/api");

app.MapAuthEndpoints();
app.MapAdminEndpoints();

api.MapGet("/services", async (AppDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Services.AsNoTracking()
        .Where(s => s.IsActive)
        .Select(s => new ServiceDto(s.Id, s.Name, s.Description,
                                    s.DurationMinutes, s.Price))
        .ToListAsync(ct)));

api.MapGet("/availability",
    async (DateOnly date, int serviceId, AvailabilityService svc, CancellationToken ct) =>
        TypedResults.Ok(await svc.GetSlotsAsync(date, serviceId, ct)));

api.MapPost("/bookings",
    async (CreateBookingRequest req, BookingService svc, CancellationToken ct) =>
    {
        var r = await svc.CreateAsync(req, ct);
        return r.IsSuccess
            ? Results.Created("/api/bookings/manage/" + r.Value!.ManageToken,
                              BookingDto.From(r.Value))
            : Results.Problem(r.Error, statusCode: StatusCodes.Status409Conflict);
    })
    .AddEndpointFilter<ValidationFilter<CreateBookingRequest>>()
    .RequireRateLimiting("booking");

api.MapGet("/bookings/manage/{token}",
    async (string token, AppDbContext db, CancellationToken ct) =>
    {
        var b = await db.Bookings.AsNoTracking().Include(x => x.Service)
            .FirstOrDefaultAsync(x => x.ManageToken == token, ct);
        return b is null ? Results.NotFound() : Results.Ok(BookingDto.From(b));
    });

api.MapPost("/bookings/manage/{token}/cancel",
    async (string token, BookingService svc, CancellationToken ct) =>
    {
        var r = await svc.CancelAsync(token, ct);
        if (r.IsSuccess) return Results.NoContent();
        return r.Error == BookingService.NotFound
            ? Results.NotFound()
            : Results.Problem(r.Error, statusCode: StatusCodes.Status409Conflict);
    })
    .RequireRateLimiting("booking");

using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    if (app.Environment.IsDevelopment())
    {
        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
    }
    await AdminSeeder.SeedAsync(sp, app.Configuration);
}

app.Run();

public partial class Program { }