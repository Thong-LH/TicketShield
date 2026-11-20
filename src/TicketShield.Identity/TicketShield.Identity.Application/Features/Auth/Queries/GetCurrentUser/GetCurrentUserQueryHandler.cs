using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.Auth.Models;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.Auth.Queries.GetCurrentUser;

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, ApiResponse<UserProfileDto>>
{
    private readonly IIdentityDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetCurrentUserQueryHandler(IIdentityDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<UserProfileDto>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.UserId.HasValue)
        {
            throw new UnauthorizedException("Session is invalid or you are not signed in.");
        }

        var userId = _currentUserService.UserId.Value;
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User", userId);
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedException("Your account has been deactivated.");
        }

        return ApiResponse<UserProfileDto>.SuccessResponse(UserProfileDto.FromUser(user), "Account profile retrieved successfully.");
    }
}
