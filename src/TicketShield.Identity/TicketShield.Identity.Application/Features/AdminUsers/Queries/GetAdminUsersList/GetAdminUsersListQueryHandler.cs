using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.AdminUsers.Models;

namespace TicketShield.Identity.Application.Features.AdminUsers.Queries.GetAdminUsersList;

public class GetAdminUsersListQueryHandler : IRequestHandler<GetAdminUsersListQuery, ApiResponse<List<AdminUserDto>>>
{
    private readonly IIdentityDbContext _context;

    public GetAdminUsersListQueryHandler(IIdentityDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<List<AdminUserDto>>> Handle(GetAdminUsersListQuery request, CancellationToken cancellationToken)
    {
        var users = await _context.Users
            .AsNoTracking()
            .Include(u => u.BankAccounts)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = users.Select(AdminUserDto.FromUser).ToList();
        return ApiResponse<List<AdminUserDto>>.SuccessResponse(result, "Lấy danh sách người dùng thành công.");
    }
}
