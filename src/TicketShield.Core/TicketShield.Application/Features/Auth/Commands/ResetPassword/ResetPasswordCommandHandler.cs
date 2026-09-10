using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Auth.Commands.ResetPassword;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, ApiResponse<string>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;

    public ResetPasswordCommandHandler(ITicketShieldDbContext dbContext, IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<ApiResponse<string>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user == null || string.IsNullOrEmpty(user.PasswordResetOtp))
        {
            throw new BusinessRuleViolationException("Mã OTP không chính xác hoặc yêu cầu đặt lại mật khẩu không tồn tại.");
        }

        if (user.PasswordResetOtpExpiresAt == null || user.PasswordResetOtpExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new BusinessRuleViolationException("Mã OTP đặt lại mật khẩu đã hết hạn. Vui lòng yêu cầu mã mới.");
        }

        if (!string.Equals(user.PasswordResetOtp.Trim(), request.Otp.Trim(), StringComparison.Ordinal))
        {
            throw new BusinessRuleViolationException("Mã OTP không chính xác. Vui lòng kiểm tra lại.");
        }

        // Update password with secure BCrypt hash and invalidate OTP
        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.PasswordResetOtp = null;
        user.PasswordResetOtpExpiresAt = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<string>.SuccessResponse(
            "Đặt lại mật khẩu thành công. Bây giờ bạn có thể đăng nhập bằng mật khẩu mới.",
            "Thành công");
    }
}
