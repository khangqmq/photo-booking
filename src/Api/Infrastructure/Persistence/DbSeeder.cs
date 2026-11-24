using Microsoft.EntityFrameworkCore;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (!await db.WorkingHours.AnyAsync())
        {
            DayOfWeek[] days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                                DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];
            foreach (var d in days)
                db.WorkingHours.Add(new WorkingHour
                    { Day = d, Start = new TimeOnly(9, 0), End = new TimeOnly(17, 0) });
        }

        if (!await db.Services.AnyAsync())
        {
            db.Services.AddRange(
                new Service { Name = "Chân dung", Description = "Chụp chân dung cá nhân",
                              DurationMinutes = 60, Price = 500000 },
                new Service { Name = "Gia đình", Description = "Chụp ảnh gia đình",
                              DurationMinutes = 90, Price = 800000 },
                new Service { Name = "Sản phẩm", Description = "Chụp ảnh sản phẩm",
                              DurationMinutes = 120, Price = 1200000 });
        }
        await db.SaveChangesAsync();
    }
}