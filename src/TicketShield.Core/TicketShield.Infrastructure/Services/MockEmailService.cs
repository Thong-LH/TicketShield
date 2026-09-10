using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;

namespace TicketShield.Infrastructure.Services;

public class MockEmailService : IEmailService
{
    private readonly ILogger<MockEmailService> _logger;

    public MockEmailService(ILogger<MockEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendPasswordResetOtpAsync(string toEmail, string fullName, string otpCode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "\n==================================================\n" +
            "📧 [MOCK EMAIL SERVICE] SENDING PASSWORD RESET OTP\n" +
            "To       : {FullName} <{ToEmail}>\n" +
            "OTP Code : {OtpCode} (Expires in 10 minutes)\n" +
            "Status   : Printed to console pending SMTP merge from team\n" +
            "==================================================",
            fullName, toEmail, otpCode);

        return Task.CompletedTask;
    }
}
