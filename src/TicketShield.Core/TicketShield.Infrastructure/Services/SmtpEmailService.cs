using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketShield.Application.Common.Configurations;
using TicketShield.Application.Common.Interfaces;

namespace TicketShield.Infrastructure.Services;

public class SmtpEmailService : IEmailService
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<SmtpSettings> options, ILogger<SmtpEmailService> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.Host))
        {
            _logger.LogInformation("================== [CORE SMTP FALLBACK] ==================");
            _logger.LogInformation("TO: {ToEmail}", toEmail);
            _logger.LogInformation("SUBJECT: {Subject}", subject);
            _logger.LogInformation("BODY:\n{HtmlBody}", htmlBody);
            _logger.LogInformation("==========================================================");
            return;
        }

        try
        {
            using var client = new SmtpClient(_settings.Host, _settings.Port)
            {
                EnableSsl = _settings.EnableSsl
            };

            if (!string.IsNullOrEmpty(_settings.Username) && !string.IsNullOrEmpty(_settings.Password))
            {
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
            }

            using var mailMessage = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            mailMessage.To.Add(toEmail);

            await client.SendMailAsync(mailMessage, cancellationToken);
            _logger.LogInformation("[SMTP] Sent email to {Email} via {Host}:{Port}", toEmail, _settings.Host, _settings.Port);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SMTP] Failed to send email to {Email}. Lock/refund status is unchanged.", toEmail);
        }
    }
}
