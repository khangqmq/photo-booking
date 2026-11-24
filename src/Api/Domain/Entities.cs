public enum BookingStatus { Pending, Confirmed, Cancelled, Completed }
 
public class Service
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}
 
public class WorkingHour
{
    public int Id { get; set; }
    public DayOfWeek Day { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
}
 
public class TimeOff
{
    public int Id { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string Reason { get; set; } = "";
}
 
public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public required string CustomerName { get; set; }
    public required string CustomerEmail { get; set; }
    public required string CustomerPhone { get; set; }
    public string? Note { get; set; }
    public required string ManageToken { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReminderSentUtc { get; set; }
    public byte[] Version { get; set; } = [];   // rowversion của SQL Server
    public List<BookingSlot> Slots { get; set; } = [];
}
 
public class BookingSlot
{
    public DateTime SlotStartUtc { get; set; }   // khóa chính: mỗi ô 30 phút chỉ thuộc một đơn
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
}
 
public class EmailOutbox
{
    public int Id { get; set; }
    public required string To { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentUtc { get; set; }
    public int Attempts { get; set; }
}
