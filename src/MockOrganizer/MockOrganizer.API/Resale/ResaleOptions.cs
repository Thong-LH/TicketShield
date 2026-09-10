using System.Net;
using System.Net.Mail;

namespace MockOrganizer.API.Resale;

public sealed class ResaleOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string HmacKey { get; set; } = "";
    public string OrganizerId { get; set; } = "";
    public int DevelopmentGrpcPort { get; set; } = 5002;
    public int OtpLifetimeSeconds { get; set; } = 300;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 5;
    public int MaxResends { get; set; } = 3;
    public int RequestsPerTicketPerHour { get; set; } = 10;
    public int RequestsPerRequesterPerHour { get; set; } = 30;
    public int FailuresPerTicketPerHour { get; set; } = 10;
    public int FailuresPerRequesterPerHour { get; set; } = 20;
    public Dictionary<string, TicketMapping> TicketMappings { get; set; } = new(StringComparer.Ordinal);
    public SmtpOptions Smtp { get; set; } = new();
}
public sealed class TicketMapping { public string EventId { get; set; } = ""; public string TierId { get; set; } = ""; }
public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Sender { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
public interface IOtpDelivery
{
    Task Send(string recipient, string otp, DateTimeOffset expires, CancellationToken ct);
}
public sealed class SmtpOtpDelivery(ResaleOptions options, IHostEnvironment environment) : IOtpDelivery
{
    public async Task Send(string recipient, string otp, DateTimeOffset expires, CancellationToken ct)
    {
        var o = options.Smtp;
        if (string.IsNullOrWhiteSpace(o.Host) || string.IsNullOrWhiteSpace(o.Sender))
            throw new InvalidOperationException("SMTP_NOT_CONFIGURED");
        if (!o.EnableSsl && !(environment.IsDevelopment() && (o.Host == "localhost" || IPAddress.TryParse(o.Host, out var ip) && IPAddress.IsLoopback(ip))))
            throw new InvalidOperationException("SMTP_TLS_REQUIRED");
        using var client = new SmtpClient(o.Host, o.Port) { EnableSsl = o.EnableSsl, UseDefaultCredentials = false };
        if (!string.IsNullOrEmpty(o.Username)) client.Credentials = new NetworkCredential(o.Username, o.Password);
        using var message = new MailMessage(o.Sender, recipient, "TicketShield resale verification",
            $"Your code to authorize listing your ticket is {otp}. Expires at {expires:O}. This does not transfer ownership.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await client.SendMailAsync(message, timeout.Token);
    }
}
