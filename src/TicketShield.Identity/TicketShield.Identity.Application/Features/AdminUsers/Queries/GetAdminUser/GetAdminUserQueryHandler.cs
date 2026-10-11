using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.AdminUsers.Models;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.AdminUsers.Queries.GetAdminUser;

public class GetAdminUserQueryHandler : IRequestHandler<GetAdminUserQuery, ApiResponse<AdminUserDto>>
{
    private readonly IIdentityDbContext _context;

    public GetAdminUserQueryHandler(IIdentityDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<AdminUserDto>> Handle(GetAdminUserQuery request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("Người dùng", request.UserId);
        }

        return ApiResponse<AdminUserDto>.SuccessResponse(AdminUserDto.FromUser(user));
    }
}
