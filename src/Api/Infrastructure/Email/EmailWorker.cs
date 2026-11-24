using Microsoft.EntityFrameworkCore;

public class EmailWorker(IServiceScopeFactory sf, ILogger<EmailWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = sf.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
 
                var pending = await db.EmailOutbox
                    .Where(e => e.SentUtc == null && e.Attempts < 5)
                    .OrderBy(e => e.Id).Take(20).ToListAsync(ct);
 
                foreach (var m in pending)
                {
                    m.Attempts++;
                    try
                    {
                        await sender.SendAsync(m.To, m.Subject, m.Body, ct);
                        m.SentUtc = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex, "Gửi mail thất bại, id {Id}", m.Id);
                    }
                }
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Email worker lỗi");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
