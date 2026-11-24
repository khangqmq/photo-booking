using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Service> Services => Set<Service>();
    public DbSet<WorkingHour> WorkingHours => Set<WorkingHour>();
    public DbSet<TimeOff> TimeOffs => Set<TimeOff>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSlot> BookingSlots => Set<BookingSlot>();
    public DbSet<EmailOutbox> EmailOutbox => Set<EmailOutbox>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Service>().Property(x => x.Price).HasPrecision(12, 0);
        b.Entity<Booking>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsRowVersion();
            e.Property(x => x.ManageToken).HasMaxLength(64);
            e.HasIndex(x => x.ManageToken).IsUnique();
            e.HasIndex(x => x.StartUtc);
        });
        b.Entity<BookingSlot>(e =>
        {
            e.HasKey(x => x.SlotStartUtc);
            e.Property(x => x.SlotStartUtc).ValueGeneratedNever();
            e.HasOne(x => x.Booking).WithMany(x => x.Slots)
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    // SQL Server không lưu Kind: ép mọi DateTime đọc từ DB về là UTC
    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<DateTime>().HaveConversion<UtcConverter>();
        c.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
    }
}

public class UtcConverter() : ValueConverter<DateTime, DateTime>(
    v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public class NullableUtcConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
