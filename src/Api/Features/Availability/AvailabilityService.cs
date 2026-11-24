using Microsoft.EntityFrameworkCore;

public class AvailabilityService(AppDbContext db, TimeProvider clock)
{
    static readonly TimeZoneInfo Vn = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    public const int StepMinutes = 30;
    const int MinLeadHours = 2;
 
    public static DateTime ToUtc(DateOnly d, TimeSpan t) =>
        TimeZoneInfo.ConvertTimeToUtc(
            d.ToDateTime(TimeOnly.FromTimeSpan(t), DateTimeKind.Unspecified), Vn);
 
    public async Task<List<DateTime>> GetSlotsAsync(
        DateOnly date, int serviceId, CancellationToken ct)
    {
        var service = await db.Services.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.IsActive, ct);
        if (service is null) return [];
 
        var hours = await db.WorkingHours.AsNoTracking()
            .Where(w => w.Day == date.DayOfWeek).ToListAsync(ct);
 
        var dayStart = ToUtc(date, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
 
        var busy = await db.Bookings.AsNoTracking()
            .Where(b => b.Status != BookingStatus.Cancelled
                     && b.StartUtc < dayEnd && b.EndUtc > dayStart)
            .Select(b => new { b.StartUtc, b.EndUtc }).ToListAsync(ct);
        var off = await db.TimeOffs.AsNoTracking()
            .Where(t => t.StartUtc < dayEnd && t.EndUtc > dayStart)
            .Select(t => new { t.StartUtc, t.EndUtc }).ToListAsync(ct);
        var blocked = busy.Concat(off).ToList();
 
        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var earliest = clock.GetUtcNow().UtcDateTime.AddHours(MinLeadHours);
        var result = new List<DateTime>();
 
        foreach (var h in hours)
        {
            for (var t = h.Start.ToTimeSpan();
                 t + duration <= h.End.ToTimeSpan();
                 t += TimeSpan.FromMinutes(StepMinutes))
            {
                var start = ToUtc(date, t);
                var end = start + duration;
                if (start < earliest) continue;
                if (blocked.Any(x => start < x.EndUtc && end > x.StartUtc)) continue;
                result.Add(start);
            }
        }
        return result;
    }
}
