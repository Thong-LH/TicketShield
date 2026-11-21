using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.Auth.Models;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.Auth.Commands.RefreshToken;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, ApiResponse<AuthResponse>>
{
    private readonly IIdentityDbContext _context;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public RefreshTokenCommandHandler(IIdentityDbContext context, IJwtTokenGenerator jwtTokenGenerator)
    {
        _context = context;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var cleanToken = request.RefreshToken.Trim();

        var existingRefreshToken = await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == cleanToken, cancellationToken);

        if (existingRefreshToken == null || !existingRefreshToken.IsActive)
        {
            throw new UnauthorizedException("Session has expired or refresh token is invalid. Please sign in again.");
        }

        var user = existingRefreshToken.User;
        if (!user.IsActive)
        {
            throw new UnauthorizedException("Your account has been suspended. Please contact administrator.");
        }

        // Token Rotation: revoke current token and generate a new one
        var newRefreshToken = _jwtTokenGenerator.GenerateRefreshToken(user.Id, request.IpAddress);
        existingRefreshToken.RevokedAt = DateTimeOffset.UtcNow;
        existingRefreshToken.RevokedByIp = request.IpAddress;
        existingRefreshToken.ReplacedByToken = newRefreshToken.Token;

        _context.RefreshTokens.Add(newRefreshToken);

        var (newAccessToken, expiresIn) = _jwtTokenGenerator.GenerateAccessToken(user);
        await _context.SaveChangesAsync(cancellationToken);

        var response = new AuthResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresIn = expiresIn,
            TokenType = "Bearer",
            User = UserProfileDto.FromUser(user)
        };

        return ApiResponse<AuthResponse>.SuccessResponse(response, "Access token refreshed successfully.");
    }
}
