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
        if (!string.IsNullOrEmpty(o.Username))
        {
            var pwd = (o.Password ?? "").Replace(" ", "");
            client.Credentials = new NetworkCredential(o.Username, pwd);
        }

        var senderAddress = new MailAddress(o.Sender, "TicketShield Organizer");
        using var message = new MailMessage(senderAddress, new MailAddress(recipient))
        {
            Subject = $"🔑 [TicketShield] Mã OTP xác thực rao bán vé chính chủ - {otp}",
            IsBodyHtml = true,
            Body = $@"
<!DOCTYPE html>
<html>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #05070a; color: #f5f5f2; padding: 24px; margin: 0;"">
  <div style=""max-width: 520px; margin: 0 auto; background-color: #0a0d12; border: 1px solid rgba(255,255,255,0.1); border-radius: 16px; padding: 32px; box-shadow: 0 10px 30px rgba(0,0,0,0.5);"">
    <div style=""border-bottom: 1px solid rgba(255,255,255,0.08); padding-bottom: 16px; margin-bottom: 24px;"">
      <span style=""color: #ff5a36; font-size: 11px; font-weight: bold; letter-spacing: 0.08em; text-transform: uppercase;"">TicketShield Verification</span>
      <h2 style=""color: #ffffff; margin: 6px 0 0 0; font-size: 20px;"">Xác Thực Quyền Sở Hữu Vé</h2>
    </div>
    <p style=""color: #8f96a3; font-size: 14px; line-height: 1.6; margin: 0 0 20px 0;"">
      Xin chào, bạn đang thực hiện đăng bán vé chính chủ trên sàn <strong style=""color: #ffffff;"">TicketShield Marketplace</strong>.
    </p>
    <div style=""background-color: #05070a; border: 1px solid rgba(255,90,54,0.3); border-radius: 12px; padding: 20px; text-align: center; margin: 24px 0;"">
      <span style=""color: #8f96a3; font-size: 11px; text-transform: uppercase; letter-spacing: 0.08em; display: block; margin-bottom: 8px;"">MÃ XÁC THỰC OTP (6 CHỮ SỐ)</span>
      <span style=""font-family: monospace; font-size: 32px; font-weight: bold; letter-spacing: 6px; color: #ff5a36;"">{otp}</span>
    </div>
    <p style=""color: #8f96a3; font-size: 12px; line-height: 1.5; margin: 16px 0 0 0;"">
      ⏱️ Mã OTP có hiệu lực trong vòng <strong style=""color: #ffffff;"">5 phút</strong>. Tuyệt đối không chia sẻ mã này cho bất kỳ ai.
    </p>
    <div style=""border-top: 1px solid rgba(255,255,255,0.08); margin-top: 24px; padding-top: 16px; color: #555c68; font-size: 11px;"">
      TicketShield Smart Escrow & Ticket Protection System
    </div>
  </div>
</body>
</html>"
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await client.SendMailAsync(message, timeout.Token);
    }
}
