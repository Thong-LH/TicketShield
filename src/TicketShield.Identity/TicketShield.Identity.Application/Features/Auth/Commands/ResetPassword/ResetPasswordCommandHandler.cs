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
            throw new BadRequestException("OTP code is invalid or has expired.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.PasswordResetOtp = null;
        user.PasswordResetOtpExpiresAt = null;

        // Invalidate all active Refresh Tokens for security
        var activeTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && r.RevokedAt == null && r.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            token.RevokedByIp = "PasswordReset";
        }

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<string>.SuccessResponse("Your password has been reset successfully. Please sign in with your new password.", "Password reset successfully.");
    }
}
