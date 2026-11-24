using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

public record CreateBookingRequest(
    int ServiceId, DateTime StartUtc,
    string CustomerName, string CustomerEmail, string CustomerPhone, string? Note);

public class BookingService(AppDbContext db, AvailabilityService availability, TimeProvider clock)
{
    public const string NotFound = "NOT_FOUND";
    static readonly TimeZoneInfo Vn = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    static string BuildConfirmationBody(Booking b, Service s)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, Vn);
        return $"""
        Xin chào {b.CustomerName},

        Bạn đã đặt lịch "{s.Name}" lúc {local:HH:mm dd/MM/yyyy} (giờ Việt Nam).
        Mã quản lý lịch của bạn: {b.ManageToken}
        """;
    }
    public async Task<Result<Booking>> CreateAsync(
        CreateBookingRequest r, CancellationToken ct)
    {
        var service = await db.Services.FindAsync([r.ServiceId], ct);
        if (service is null || !service.IsActive)
            return Result<Booking>.Fail("Gói chụp không tồn tại");

        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(r.StartUtc, Vn));
        var slots = await availability.GetSlotsAsync(localDate, r.ServiceId, ct);
        if (!slots.Contains(r.StartUtc))
            return Result<Booking>.Fail("Khung giờ này không còn trống");

        var booking = new Booking
        {
            ServiceId = r.ServiceId,
            StartUtc = r.StartUtc,
            EndUtc = r.StartUtc.AddMinutes(service.DurationMinutes),
            CustomerName = r.CustomerName.Trim(),
            CustomerEmail = r.CustomerEmail.Trim().ToLowerInvariant(),
            CustomerPhone = r.CustomerPhone.Trim(),
            Note = r.Note,
            ManageToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
        };
        // giữ từng ô 30 phút mà đơn này chiếm; trùng ô nào thì DB từ chối
        for (var t = booking.StartUtc; t < booking.EndUtc;
             t = t.AddMinutes(AvailabilityService.StepMinutes))
            booking.Slots.Add(new BookingSlot { SlotStartUtc = t });
        db.Bookings.Add(booking);
        db.EmailOutbox.Add(new EmailOutbox
        {
            To = booking.CustomerEmail,
            Subject = "Xác nhận đặt lịch chụp hình",
            Body = BuildConfirmationBody(booking, service),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqlException { Number: 2627 or 2601 or 1205 })
        {
            return Result<Booking>.Fail("Vừa có người đặt khung giờ này");
        }
        return Result<Booking>.Ok(booking);
    }

    public async Task<Result<Booking>> CancelAsync(string token, CancellationToken ct)
    {
        var booking = await db.Bookings.Include(b => b.Slots)
            .FirstOrDefaultAsync(b => b.ManageToken == token, ct);
        if (booking is null) return Result<Booking>.Fail(NotFound);

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed)
            return Result<Booking>.Fail("Đơn này không thể hủy");
        if (booking.StartUtc - clock.GetUtcNow().UtcDateTime < TimeSpan.FromHours(24))
            return Result<Booking>.Fail("Chỉ được hủy trước giờ chụp ít nhất 24 giờ");

        booking.Status = BookingStatus.Cancelled;
        db.BookingSlots.RemoveRange(booking.Slots);   // nhả khung giờ
        db.EmailOutbox.Add(new EmailOutbox
        {
            To = booking.CustomerEmail,
            Subject = "Đã hủy lịch chụp hình",
            Body = $"Lịch chụp của bạn lúc {TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, Vn):HH:mm dd/MM/yyyy} đã được hủy.",
        });
        await db.SaveChangesAsync(ct);
        return Result<Booking>.Ok(booking);
    }

}
