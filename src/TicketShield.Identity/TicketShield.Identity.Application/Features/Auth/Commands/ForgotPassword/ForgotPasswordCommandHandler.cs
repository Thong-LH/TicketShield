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

        // Security best practice: Không tiết lộ email có tồn tại hay không
        if (user != null && user.IsActive)
        {
            var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.PasswordResetOtp = otp;
            user.PasswordResetOtpExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);

            await _context.SaveChangesAsync(cancellationToken);

            var subject = "[TicketShield] Mã xác thực đặt lại mật khẩu của bạn";
            var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px;'>
                    <h2>Đặt lại mật khẩu TicketShield</h2>
                    <p>Xin chào <strong>{user.FullName}</strong>,</p>
                    <p>Mã OTP để đặt lại mật khẩu của bạn là:</p>
                    <h1 style='color: #2563eb; letter-spacing: 5px;'>{otp}</h1>
                    <p>Mã xác thực có hiệu lực trong vòng <strong>10 phút</strong>. Tuyệt đối không chia sẻ mã này cho bất kỳ ai.</p>
                </div>";

            await _emailService.SendEmailAsync(user.Email, subject, htmlBody, cancellationToken);
        }

        return ApiResponse<string>.SuccessResponse(
            "Nếu email tồn tại trong hệ thống, mã xác thực OTP đã được gửi đến hộp thư của bạn.",
            "Yêu cầu đặt lại mật khẩu đã được tiếp nhận.");
    }
}
