using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;

namespace TicketShield.Identity.Application.Features.Auth.Commands.ForgotPassword;

public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, ApiResponse<string>>
{
    private readonly IIdentityDbContext _context;
    private readonly IEmailService _emailService;

    public ForgotPasswordCommandHandler(IIdentityDbContext context, IEmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    public async Task<ApiResponse<string>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var cleanEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail, cancellationToken);

        // Security best practice: Do not disclose whether email exists
        if (user != null && user.IsActive)
        {
            var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.PasswordResetOtp = otp;
            user.PasswordResetOtpExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);

            await _context.SaveChangesAsync(cancellationToken);

            var subject = "[TicketShield] Password reset verification code";
            var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px;'>
                    <h2>TicketShield Password Reset</h2>
                    <p>Hello <strong>{user.FullName}</strong>,</p>
                    <p>Your password reset OTP code is:</p>
                    <h1 style='color: #2563eb; letter-spacing: 5px;'>{otp}</h1>
                    <p>This verification code is valid for <strong>10 minutes</strong>. Never share this code with anyone.</p>
                </div>";

            await _emailService.SendEmailAsync(user.Email, subject, htmlBody, cancellationToken);
        }

        return ApiResponse<string>.SuccessResponse(
            "If this email exists in our system, a verification OTP code has been sent to your inbox.",
            "Password reset request received.");
    }
}
