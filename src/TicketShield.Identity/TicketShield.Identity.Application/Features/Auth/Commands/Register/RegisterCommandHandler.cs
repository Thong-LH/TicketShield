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

namespace TicketShield.Identity.Application.Features.Auth.Commands.Register;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, ApiResponse<AuthResponse>>
{
    private readonly IIdentityDbContext _context;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IPublishEndpoint _publishEndpoint;

    public RegisterCommandHandler(
        IIdentityDbContext context,
        IJwtTokenGenerator jwtTokenGenerator,
        IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _jwtTokenGenerator = jwtTokenGenerator;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var cleanEmail = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail, cancellationToken);

        if (existingUser != null)
        {
            throw new BadRequestException("Email này đã được sử dụng. Vui lòng chọn một email khác.");
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = cleanEmail,
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            IdCardNumber = request.IdCardNumber?.Trim(),
            PasswordHash = passwordHash,
            Role = UserRole.User,
            IsActive = true
        };

        _context.Users.Add(user);

        var (accessToken, expiresIn) = _jwtTokenGenerator.GenerateAccessToken(user);
        var refreshToken = _jwtTokenGenerator.GenerateRefreshToken(user.Id);

        _context.RefreshTokens.Add(refreshToken);

        // Publish UserCreatedEvent via Transactional Outbox
        await _publishEndpoint.Publish<IUserCreatedEvent>(new UserCreatedEvent(
            UserId: user.Id,
            Email: user.Email,
            FullName: user.FullName,
            PhoneNumber: user.PhoneNumber,
            Role: user.Role.ToString(),
            CreatedAt: user.CreatedAt
        ), cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        var response = new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresIn = expiresIn,
            TokenType = "Bearer",
            User = UserProfileDto.FromUser(user)
        };

        return ApiResponse<AuthResponse>.SuccessResponse(response, "Đăng ký tài khoản thành công.");
    }
}
