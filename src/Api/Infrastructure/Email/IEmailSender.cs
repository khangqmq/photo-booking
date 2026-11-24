public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

public class EmailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public bool RequireTls { get; set; } = true;
}