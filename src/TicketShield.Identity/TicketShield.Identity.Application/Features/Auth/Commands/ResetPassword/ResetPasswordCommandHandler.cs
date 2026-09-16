using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.Auth.Commands.ResetPassword;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, ApiResponse<string>>
{
    private readonly IIdentityDbContext _context;

    public ResetPasswordCommandHandler(IIdentityDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<string>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var cleanEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail, cancellationToken);

        if (user == null ||
            string.IsNullOrEmpty(user.PasswordResetOtp) ||
            user.PasswordResetOtp != request.Otp.Trim() ||
            user.PasswordResetOtpExpiresAt == null ||
            user.PasswordResetOtpExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new BadRequestException("Mã OTP không chính xác hoặc đã hết hiệu lực.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.PasswordResetOtp = null;
        user.PasswordResetOtpExpiresAt = null;

        // Vô hiệu hóa toàn bộ Refresh Tokens cũ của User để đảm bảo an toàn sau khi đổi mật khẩu
        var activeTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && r.RevokedAt == null && r.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            token.RevokedByIp = "PasswordReset";
        }

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<string>.SuccessResponse("Mật khẩu của bạn đã được đặt lại thành công. Vui lòng đăng nhập với mật khẩu mới.", "Đặt lại mật khẩu thành công.");
    }
}
