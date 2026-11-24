public record BookingDto(Guid Id, int ServiceId, string? ServiceName,
    DateTime StartUtc, DateTime EndUtc, string Status, string CustomerName, string? Note)
{
    public static BookingDto From(Booking b) => new(
        b.Id, b.ServiceId, b.Service?.Name, b.StartUtc, b.EndUtc,
        b.Status.ToString(), b.CustomerName, b.Note);
}