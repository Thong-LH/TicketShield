using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Organizers.Models;

namespace TicketShield.Application.Features.Admin.Organizers.Queries.GetAdminOrganizers;

public class GetAdminOrganizersQueryHandler(ITicketShieldDbContext dbContext)
    : IRequestHandler<GetAdminOrganizersQuery, ApiResponse<List<AdminOrganizerDto>>>
{
    public async Task<ApiResponse<List<AdminOrganizerDto>>> Handle(
        GetAdminOrganizersQuery request,
        CancellationToken ct)
    {
        var organizers = await dbContext.Organizers
            .AsNoTracking()
            .Include(o => o.Events)
                .ThenInclude(e => e.TicketTiers)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new AdminOrganizerDto(
                o.Id,
                o.Name,
                o.OfficialEmail,
                o.ContactPhone,
                o.TaxCode,
                o.ContactPerson,
                o.Status,
                o.ApiBaseUrl,
                o.GrpcEndpoint,
                o.WebhookUrl,
                o.WebhookSecretKey,
                o.ConnectionStatus,
                o.Events.Count,
                o.LastPingAt,
                o.LastPingLatencyMs,
                o.LastCatalogSyncedAt,
                o.CreatedAt,
                o.UpdatedAt,
                o.Events.Select(e => new AdminOrganizerEventItemDto(
                    e.Id,
                    e.Name,
                    e.Artist,
                    e.Venue,
                    e.City,
                    e.EventStartAt,
                    e.Status,
                    e.TicketTiers.Count
                )).ToList()
            ))
            .ToListAsync(ct);

        return ApiResponse<List<AdminOrganizerDto>>.SuccessResponse(
            organizers,
            "Lấy danh sách ban tổ chức đối tác thành công.");
    }
}
