using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketShield.Identity.Application.Common.Interfaces;

namespace TicketShield.Identity.Infrastructure.Services;

public class MockEmailService : IEmailService
{
    private readonly ILogger<MockEmailService> _logger;
    private readonly IConfiguration _configuration;

    public MockEmailService(ILogger<MockEmailService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var smtpHost = _configuration["Smtp:Host"];
        var smtpPortStr = _configuration["Smtp:Port"];
        var sender = _configuration["Smtp:Sender"] ?? "no-reply@ticketshield.vn";
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];
        var enableSsl = _configuration.GetValue<bool>("Smtp:EnableSsl", true);

        if (!string.IsNullOrEmpty(smtpHost) && int.TryParse(smtpPortStr, out var smtpPort))
        {
            try
            {
                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    EnableSsl = enableSsl
                };

                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(username, password);
                }

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(sender, "TicketShield Security"),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(toEmail);

                await client.SendMailAsync(mailMessage, cancellationToken);
                _logger.LogInformation("[SMTP] Đã gửi email tới {Email} qua {Host}:{Port}", toEmail, smtpHost, smtpPort);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SMTP] Gửi email qua SMTP thất bại, ghi log dự phòng ra console.");
            }
        }

        _logger.LogInformation("================== [MOCK EMAIL SERVICE] ==================");
        _logger.LogInformation("TO: {ToEmail}", toEmail);
        _logger.LogInformation("SUBJECT: {Subject}", subject);
        _logger.LogInformation("BODY:\n{HtmlBody}", htmlBody);
        _logger.LogInformation("==========================================================");

        await Task.CompletedTask;
    }
}
