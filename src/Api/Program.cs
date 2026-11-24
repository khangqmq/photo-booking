using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

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
            PermitLimit = 5, Window = TimeSpan.FromMinutes(1),
        }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
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

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.Run();

public partial class Program { }