using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Contracts.Events;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.AdminUsers.Models;
using TicketShield.Identity.Domain.Enums;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.AdminUsers.Commands.SuspendUser;

public class SuspendUserCommandHandler : IRequestHandler<SuspendUserCommand, ApiResponse<AdminUserDto>>
{
    private readonly IIdentityDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPublishEndpoint _publishEndpoint;

    public SuspendUserCommandHandler(
        IIdentityDbContext context,
        ICurrentUserService currentUser,
        IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _currentUser = currentUser;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<ApiResponse<AdminUserDto>> Handle(SuspendUserCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } callerId || callerId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để khóa tài khoản.");
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("Người dùng", request.UserId);
        }

        if (callerId == user.Id)
        {
            throw new ForbiddenAccessException("Bạn không thể khóa chính tài khoản của mình.");
        }

        if (user.Role != UserRole.User)
        {
            throw new ForbiddenAccessException("Chỉ được khóa tài khoản người dùng.");
        }

        user.IsActive = false;
        user.TrustScore = 0;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var activeTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && r.RevokedAt == null && r.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            token.RevokedByIp = "SuspendUser";
        }

        await _publishEndpoint.Publish<IUserProfileUpdatedEvent>(new UserProfileUpdatedEvent(
            UserId: user.Id,
            FullName: user.FullName,
            PhoneNumber: user.PhoneNumber,
            Role: user.Role.ToString(),
            IsActive: user.IsActive,
            UpdatedAt: user.UpdatedAt
        ), cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<AdminUserDto>.SuccessResponse(AdminUserDto.FromUser(user), "Đã khóa tài khoản.");
    }
}
