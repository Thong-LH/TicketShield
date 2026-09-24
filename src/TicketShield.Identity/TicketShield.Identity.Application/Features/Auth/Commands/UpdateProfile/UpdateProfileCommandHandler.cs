using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Contracts.Events;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.Auth.Models;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.Auth.Commands.UpdateProfile;

public class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, ApiResponse<UserProfileDto>>
{
    private readonly IIdentityDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPublishEndpoint _publishEndpoint;

    public UpdateProfileCommandHandler(
        IIdentityDbContext context,
        ICurrentUserService currentUserService,
        IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _currentUserService = currentUserService;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<ApiResponse<UserProfileDto>> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var userId = request.UserId ?? _currentUserService.UserId;
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            throw new UnauthorizedException("Phiên làm việc không hợp lệ hoặc bạn chưa đăng nhập.");
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("Người dùng", userId.Value);
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedException("Tài khoản của bạn đã bị vô hiệu hóa.");
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = request.PhoneNumber?.Trim();
        user.IdCardNumber = request.IdCardNumber?.Trim();
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // Publish UserProfileUpdatedEvent via Transactional Outbox
        await _publishEndpoint.Publish<IUserProfileUpdatedEvent>(new UserProfileUpdatedEvent(
            UserId: user.Id,
            FullName: user.FullName,
            PhoneNumber: user.PhoneNumber,
            Role: user.Role.ToString(),
            IsActive: user.IsActive,
            UpdatedAt: user.UpdatedAt
        ), cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<UserProfileDto>.SuccessResponse(
            UserProfileDto.FromUser(user),
            "Cập nhật thông tin tài khoản thành công."
        );
    }
}
