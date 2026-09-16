namespace TicketShield.Application.Common.Configurations;

public class SmtpSettings
{
    public const string SectionName = "SmtpSettings";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string SenderEmail { get; set; } = "ticketshieldai@gmail.com";
    public string SenderName { get; set; } = "TicketShield AI";
    public string Username { get; set; } = "ticketshieldai@gmail.com";
    public string Password { get; set; } = string.Empty;
}
