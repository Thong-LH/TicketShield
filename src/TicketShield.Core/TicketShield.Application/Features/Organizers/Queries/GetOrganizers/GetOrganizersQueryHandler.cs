using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Organizers.Queries.GetOrganizers;

/// <summary>
/// Handler xử lý truy vấn danh sách Ban tổ chức hợp tác
/// </summary>
public class GetOrganizersQueryHandler : IRequestHandler<GetOrganizersQuery, ApiResponse<List<OrganizerDto>>>
{
    private readonly ITicketShieldDbContext _dbContext;

    public GetOrganizersQueryHandler(ITicketShieldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<List<OrganizerDto>>> Handle(GetOrganizersQuery request, CancellationToken cancellationToken)
    {
        var organizers = await _dbContext.Organizers
            .AsNoTracking()
            .Where(o => o.Status == "ACTIVE")
            .OrderBy(o => o.Name)
            .Select(o => new OrganizerDto(
                o.Id,
                o.Name,
                o.Name.Replace(" ", "").ToUpperInvariant(),
                null,
                o.OfficialEmail,
                o.Status
            ))
            .ToListAsync(cancellationToken);

        return ApiResponse<List<OrganizerDto>>.SuccessResponse(organizers, "Lấy danh sách ban tổ chức thành công.");
    }
}
