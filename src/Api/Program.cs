using FluentValidation;
using Scalar.AspNetCore;
using Serilog;
using Microsoft.EntityFrameworkCore;

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
builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()!)
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<BookingService>();

var app = builder.Build();
 
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseCors("web");
 
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

// TẠM THỜI: xóa ở Giai đoạn 4
app.MapGet("/dev/slots", async (DateOnly date, int serviceId,
    AvailabilityService a, CancellationToken ct) => await a.GetSlotsAsync(date, serviceId, ct));
app.MapPost("/dev/book", async (CreateBookingRequest r, BookingService s, CancellationToken ct) =>
{
    var res = await s.CreateAsync(r, ct);
    return res.IsSuccess
        ? Results.Ok(new { res.Value!.Id, res.Value.ManageToken })
        : Results.Conflict(res.Error);
});
app.MapPost("/dev/cancel/{token}", async (string token, BookingService s, CancellationToken ct) =>
{
    var res = await s.CancelAsync(token, ct);
    return res.IsSuccess ? Results.Ok()
        : res.Error == BookingService.NotFound ? Results.NotFound() : Results.BadRequest(res.Error);
});

app.Run();
 
public partial class Program { }   
