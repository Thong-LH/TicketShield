using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Auth.Commands.ForgotPassword;

public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, ApiResponse<string>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly IEmailService _emailService;

    public ForgotPasswordCommandHandler(ITicketShieldDbContext dbContext, IEmailService emailService)
    {
        _dbContext = dbContext;
        _emailService = emailService;
    }

    public async Task<ApiResponse<string>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        // Security best practice: Don't reveal whether user exists (OWASP anti-enumeration)
        if (user != null && user.IsActive)
        {
            // Generate 6-digit numeric OTP
            var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
            user.PasswordResetOtp = otp;
            user.PasswordResetOtpExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
            user.UpdatedAt = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Send via Email Service (Mock logs to console pending SMTP merge)
            await _emailService.SendPasswordResetOtpAsync(user.Email, user.FullName, otp, cancellationToken);
        }

        return ApiResponse<string>.SuccessResponse(
            "Nếu email của bạn tồn tại trong hệ thống, mã OTP đặt lại mật khẩu đã được gửi đến hộp thư.",
            "Yêu cầu đặt lại mật khẩu đã được tiếp nhận.");
    }
}
