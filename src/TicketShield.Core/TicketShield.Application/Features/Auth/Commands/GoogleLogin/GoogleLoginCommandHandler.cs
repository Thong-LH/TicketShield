using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Auth.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Auth.Commands.GoogleLogin;

public class GoogleLoginCommandHandler : IRequestHandler<GoogleLoginCommand, ApiResponse<AuthResponse>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly IGoogleAuthService _googleAuthService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public GoogleLoginCommandHandler(
        ITicketShieldDbContext dbContext,
        IGoogleAuthService googleAuthService,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _googleAuthService = googleAuthService;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
    {
        var googleUser = await _googleAuthService.VerifyIdTokenAsync(request.IdToken, cancellationToken);
        var normalizedEmail = googleUser.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.GoogleId == googleUser.GoogleId || u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user == null)
        {
            // Auto-register user from Google profile
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                FullName = googleUser.FullName,
                GoogleId = googleUser.GoogleId,
                Role = UserRole.User,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (!user.IsActive)
            {
                throw new BusinessRuleViolationException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ CSKH TicketShield.");
            }

            // Link GoogleId if not already set
            if (string.IsNullOrEmpty(user.GoogleId))
            {
                user.GoogleId = googleUser.GoogleId;
                user.UpdatedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        var token = _jwtTokenGenerator.GenerateToken(user.Id, user.Email, user.FullName, user.Role.ToString());

        var response = new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            Token = token
        };

        return ApiResponse<AuthResponse>.SuccessResponse(response, "Đăng nhập bằng Google thành công.");
    }
}
