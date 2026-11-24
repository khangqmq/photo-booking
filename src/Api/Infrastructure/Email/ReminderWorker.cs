using Microsoft.EntityFrameworkCore;

public class ReminderWorker(IServiceScopeFactory sf, TimeProvider clock,
    IConfiguration config, ILogger<ReminderWorker> log) : BackgroundService
{
    static readonly TimeZoneInfo Vn = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        do
        {
            try
            {
                using var scope = sf.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = clock.GetUtcNow().UtcDateTime;
                var from = now.AddHours(23);
                var to = now.AddHours(25);
                var baseUrl = (config["App:PublicBaseUrl"] ?? "").TrimEnd('/');

                var due = await db.Bookings.Include(b => b.Service)
                    .Where(b => b.Status == BookingStatus.Confirmed
                             && b.ReminderSentUtc == null
                             && b.StartUtc >= from && b.StartUtc <= to)
                    .ToListAsync(ct);

                foreach (var b in due)
                {
                    var local = TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, Vn);
                    db.EmailOutbox.Add(new EmailOutbox
                    {
                        To = b.CustomerEmail,
                        Subject = "Nhắc lịch chụp hình vào ngày mai",
                        Body = $"""
                            Xin chào {b.CustomerName},

                            Nhắc bạn lịch chụp "{b.Service.Name}" lúc {local:HH:mm dd/MM/yyyy}.
                            Xem hoặc hủy lịch tại: {baseUrl}/manage/{b.ManageToken}
                            """,
                    });
                    b.ReminderSentUtc = now;   // lưu cùng lúc với email: không bao giờ nhắc hai lần
                }
                if (due.Count > 0) await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Reminder worker lỗi");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}