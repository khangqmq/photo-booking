using FluentValidation;
using Microsoft.EntityFrameworkCore;

public record AdminServiceDto(int Id, string Name, string Description,
    int DurationMinutes, decimal Price, bool IsActive);
public record ServiceInput(string Name, int DurationMinutes, decimal Price,
    bool IsActive = true, string Description = "");
public record BookingActionRequest(string? Version, string? Reason);
public record WorkingHourDto(DayOfWeek Day, TimeOnly Start, TimeOnly End);
public record TimeOffInput(DateTime StartUtc, DateTime EndUtc, string? Reason);
public record AdminBookingDto(Guid Id, string ServiceName, DateTime StartUtc, DateTime EndUtc,
    string Status, string CustomerName, string CustomerEmail, string CustomerPhone,
    string? Note, string Version)
{
    public static AdminBookingDto From(Booking b) => new(b.Id, b.Service.Name, b.StartUtc,
        b.EndUtc, b.Status.ToString(), b.CustomerName, b.CustomerEmail, b.CustomerPhone,
        b.Note, Convert.ToBase64String(b.Version));
}

public class ServiceInputValidator : AbstractValidator<ServiceInput>
{
    public ServiceInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(30, 480)
            .Must(d => d % AvailabilityService.StepMinutes == 0)
            .WithMessage("Thời lượng phải là bội số của 30 phút");
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
    }
}

public static class AdminEndpoints
{
    static IResult ToResult(Result<Booking> r) =>
        r.IsSuccess ? Results.NoContent()
        : r.Error == BookingService.NotFound ? Results.NotFound()
        : r.Error == BookingService.Conflict
            ? Results.Problem("Đơn vừa được người khác thay đổi, hãy tải lại danh sách",
                              statusCode: StatusCodes.Status409Conflict)
            : Results.Problem(r.Error, statusCode: StatusCodes.Status400BadRequest);

    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization("Admin");

        // ----- Gói chụp -----
        admin.MapGet("/services", async (AppDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.Services.AsNoTracking().OrderBy(s => s.Id)
                .Select(s => new AdminServiceDto(s.Id, s.Name, s.Description,
                                                 s.DurationMinutes, s.Price, s.IsActive))
                .ToListAsync(ct)));

        admin.MapPost("/services", async (ServiceInput i, AppDbContext db, CancellationToken ct) =>
        {
            var s = new Service { Name = i.Name.Trim(), Description = i.Description,
                DurationMinutes = i.DurationMinutes, Price = i.Price, IsActive = i.IsActive };
            db.Services.Add(s);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/services/{s.Id}",
                new AdminServiceDto(s.Id, s.Name, s.Description, s.DurationMinutes, s.Price, s.IsActive));
        }).AddEndpointFilter<ValidationFilter<ServiceInput>>();

        admin.MapPut("/services/{id:int}",
            async (int id, ServiceInput i, AppDbContext db, CancellationToken ct) =>
        {
            var s = await db.Services.FindAsync([id], ct);
            if (s is null) return Results.NotFound();
            s.Name = i.Name.Trim(); s.Description = i.Description;
            s.DurationMinutes = i.DurationMinutes; s.Price = i.Price; s.IsActive = i.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AddEndpointFilter<ValidationFilter<ServiceInput>>();

        // ----- Đơn đặt lịch -----
        admin.MapGet("/bookings", async (DateOnly from, DateOnly to, AppDbContext db, CancellationToken ct) =>
        {
            var fromUtc = AvailabilityService.ToUtc(from, TimeSpan.Zero);
            var toUtc = AvailabilityService.ToUtc(to.AddDays(1), TimeSpan.Zero);
            var list = await db.Bookings.AsNoTracking().Include(b => b.Service)
                .Where(b => b.StartUtc >= fromUtc && b.StartUtc < toUtc)
                .OrderBy(b => b.StartUtc).ToListAsync(ct);
            return TypedResults.Ok(list.Select(AdminBookingDto.From));
        });

        admin.MapPost("/bookings/{id:guid}/confirm",
            async (Guid id, BookingActionRequest? req, BookingService svc, CancellationToken ct) =>
                ToResult(await svc.ConfirmAsync(id, req?.Version, ct)));

        admin.MapPost("/bookings/{id:guid}/cancel",
            async (Guid id, BookingActionRequest? req, BookingService svc, CancellationToken ct) =>
                ToResult(await svc.AdminCancelAsync(id, req?.Version, req?.Reason, ct)));

        // ----- Giờ làm việc -----
        admin.MapGet("/working-hours", async (AppDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.WorkingHours.AsNoTracking().OrderBy(w => w.Day)
                .Select(w => new WorkingHourDto(w.Day, w.Start, w.End)).ToListAsync(ct)));

        admin.MapPut("/working-hours", async (List<WorkingHourDto> items, AppDbContext db, CancellationToken ct) =>
        {
            if (items.Any(i => i.Start >= i.End))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["items"] = ["Giờ mở cửa phải trước giờ đóng cửa"] });
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.WorkingHours.ExecuteDeleteAsync(ct);
            db.WorkingHours.AddRange(items.Select(i =>
                new WorkingHour { Day = i.Day, Start = i.Start, End = i.End }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        });

        // ----- Ngày nghỉ -----
        admin.MapGet("/time-off", async (AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            return TypedResults.Ok(await db.TimeOffs.AsNoTracking()
                .Where(t => t.EndUtc > now).OrderBy(t => t.StartUtc).ToListAsync(ct));
        });

        admin.MapPost("/time-off", async (TimeOffInput i, AppDbContext db, CancellationToken ct) =>
        {
            if (i.EndUtc <= i.StartUtc)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["endUtc"] = ["Giờ kết thúc phải sau giờ bắt đầu"] });
            var t = new TimeOff { StartUtc = i.StartUtc, EndUtc = i.EndUtc, Reason = i.Reason ?? "" };
            db.TimeOffs.Add(t);
            await db.SaveChangesAsync(ct);
            // Không tự hủy: chỉ liệt kê các đơn bị ảnh hưởng để admin xử lý
            var affected = await db.Bookings.AsNoTracking().Include(b => b.Service)
                .Where(b => b.Status != BookingStatus.Cancelled
                         && b.StartUtc < t.EndUtc && b.EndUtc > t.StartUtc)
                .ToListAsync(ct);
            return Results.Created($"/api/admin/time-off/{t.Id}",
                new { t.Id, affectedBookings = affected.Select(AdminBookingDto.From) });
        });

        admin.MapDelete("/time-off/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
            await db.TimeOffs.Where(t => t.Id == id).ExecuteDeleteAsync(ct) == 0
                ? Results.NotFound() : Results.NoContent());
    }
}