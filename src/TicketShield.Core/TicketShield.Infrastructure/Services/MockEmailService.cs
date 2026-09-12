using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;

namespace TicketShield.Infrastructure.Services;

public class MockEmailService : IEmailService
{
    private readonly ILogger<MockEmailService> _logger;
    private readonly IConfiguration? _configuration;

    public MockEmailService(ILogger<MockEmailService> logger, IConfiguration? configuration = null)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task SendPasswordResetOtpAsync(string toEmail, string fullName, string otpCode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "\n==================================================\n" +
            "📧 [EMAIL SERVICE] SENDING PASSWORD RESET OTP\n" +
            "To       : {FullName} <{ToEmail}>\n" +
            "OTP Code : {OtpCode} (Expires in 10 minutes)\n" +
            "==================================================",
            fullName, toEmail, otpCode);

        if (_configuration == null)
        {
            return;
        }

        var host = _configuration["Smtp:Host"] ?? _configuration["OrganizerResale:Smtp:Host"];
        var username = _configuration["Smtp:Username"] ?? _configuration["OrganizerResale:Smtp:Username"];
        var password = (_configuration["Smtp:Password"] ?? _configuration["OrganizerResale:Smtp:Password"] ?? "").Replace(" ", "");
        var sender = _configuration["Smtp:Sender"] ?? _configuration["OrganizerResale:Smtp:Sender"] ?? username;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        try
        {
            using var client = new SmtpClient(host, 587)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(username, password)
            };

            using var message = new MailMessage
            {
                From = new MailAddress(sender, "TicketShield Support"),
                Subject = "🔐 [TicketShield] Mã OTP khôi phục mật khẩu",
                Body = $@"Xin chào {fullName},

Mã OTP để đặt lại mật khẩu cho tài khoản TicketShield của bạn là:

👉 OTP CODE: {otpCode}

Mã có hiệu lực trong 10 phút. Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này.

Trân trọng,
TicketShield Security Team",
                IsBodyHtml = false
            };
            message.To.Add(toEmail);

            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Password Reset OTP email to {ToEmail}", toEmail);
        }
    }
}
