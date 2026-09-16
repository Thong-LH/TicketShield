using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Contracts.Events;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.Auth.Models;
using TicketShield.Identity.Domain.Entities;
using TicketShield.Identity.Domain.Enums;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.Auth.Commands.GoogleLogin;

public class GoogleLoginCommandHandler : IRequestHandler<GoogleLoginCommand, ApiResponse<AuthResponse>>
{
    private readonly IIdentityDbContext _context;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IGoogleAuthService _googleAuthService;
    private readonly IPublishEndpoint _publishEndpoint;

    public GoogleLoginCommandHandler(
        IIdentityDbContext context,
        IJwtTokenGenerator jwtTokenGenerator,
        IGoogleAuthService googleAuthService,
        IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _jwtTokenGenerator = jwtTokenGenerator;
        _googleAuthService = googleAuthService;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
    {
        var googleUser = await _googleAuthService.VerifyGoogleTokenAsync(request.IdToken, cancellationToken);
        if (googleUser == null)
        {
            throw new UnauthorizedException("Mã xác thực Google IdToken không hợp lệ hoặc đã hết hạn.");
        }

        var cleanEmail = googleUser.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.GoogleId == googleUser.GoogleId || u.Email.ToLower() == cleanEmail, cancellationToken);

        var isNewUser = false;

        if (user == null)
        {
            isNewUser = true;
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = cleanEmail,
                FullName = string.IsNullOrWhiteSpace(googleUser.Name) ? cleanEmail : googleUser.Name,
                GoogleId = googleUser.GoogleId,
                Role = UserRole.User,
                IsActive = true
            };
            _context.Users.Add(user);
        }
        else
        {
            if (string.IsNullOrEmpty(user.GoogleId))
            {
                user.GoogleId = googleUser.GoogleId;
            }
            if (!user.IsActive)
            {
                throw new UnauthorizedException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
            }
        }

        var (accessToken, expiresIn) = _jwtTokenGenerator.GenerateAccessToken(user);
        var refreshToken = _jwtTokenGenerator.GenerateRefreshToken(user.Id);

        _context.RefreshTokens.Add(refreshToken);

        if (isNewUser)
        {
            await _publishEndpoint.Publish<IUserCreatedEvent>(new UserCreatedEvent(
                UserId: user.Id,
                Email: user.Email,
                FullName: user.FullName,
                PhoneNumber: user.PhoneNumber,
                Role: user.Role.ToString(),
                CreatedAt: user.CreatedAt
            ), cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var response = new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresIn = expiresIn,
            TokenType = "Bearer",
            User = UserProfileDto.FromUser(user)
        };

        return ApiResponse<AuthResponse>.SuccessResponse(response, "Đăng nhập Google thành công.");
    }
}
