using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.Auth.Models;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, ApiResponse<AuthResponse>>
{
    private readonly IIdentityDbContext _context;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public LoginCommandHandler(IIdentityDbContext context, IJwtTokenGenerator jwtTokenGenerator)
    {
        _context = context;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var cleanEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail, cancellationToken);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid email address or password.");
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedException("Your account has been suspended. Please contact administrator.");
        }

        var isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!isPasswordValid)
        {
            throw new UnauthorizedException("Invalid email address or password.");
        }

        var (accessToken, expiresIn) = _jwtTokenGenerator.GenerateAccessToken(user);
        var refreshToken = _jwtTokenGenerator.GenerateRefreshToken(user.Id);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        var response = new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresIn = expiresIn,
            TokenType = "Bearer",
            User = UserProfileDto.FromUser(user)
        };

        return ApiResponse<AuthResponse>.SuccessResponse(response, "Signed in successfully.");
    }
}
