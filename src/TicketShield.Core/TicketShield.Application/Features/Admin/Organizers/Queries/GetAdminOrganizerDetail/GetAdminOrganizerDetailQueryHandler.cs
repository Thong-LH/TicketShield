using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Organizers.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Admin.Organizers.Queries.GetAdminOrganizerDetail;

public class GetAdminOrganizerDetailQueryHandler(ITicketShieldDbContext dbContext)
    : IRequestHandler<GetAdminOrganizerDetailQuery, ApiResponse<AdminOrganizerDto>>
{
    public async Task<ApiResponse<AdminOrganizerDto>> Handle(
        GetAdminOrganizerDetailQuery request,
        CancellationToken ct)
    {
        var organizer = await dbContext.Organizers
            .AsNoTracking()
            .Include(o => o.Events)
                .ThenInclude(e => e.TicketTiers)
            .FirstOrDefaultAsync(o => o.Id == request.Id, ct);

        if (organizer == null)
        {
            throw new NotFoundException($"Không tìm thấy ban tổ chức với ID '{request.Id}'.");
        }

        var dto = new AdminOrganizerDto(
            organizer.Id,
            organizer.Name,
            organizer.OfficialEmail,
            organizer.ContactPhone,
            organizer.TaxCode,
            organizer.ContactPerson,
            organizer.Status,
            organizer.ApiBaseUrl,
            organizer.GrpcEndpoint,
            organizer.WebhookUrl,
            organizer.WebhookSecretKey,
            organizer.ConnectionStatus,
            organizer.Events.Count,
            organizer.LastPingAt,
            organizer.LastPingLatencyMs,
            organizer.LastCatalogSyncedAt,
            organizer.CreatedAt,
            organizer.UpdatedAt,
            organizer.Events.Select(e => new AdminOrganizerEventItemDto(
                e.Id,
                e.Name,
                e.Artist,
                e.Venue,
                e.City,
                e.EventStartAt,
                e.Status,
                e.TicketTiers.Count
            )).ToList()
        );

        return ApiResponse<AdminOrganizerDto>.SuccessResponse(dto, "Lấy thông tin ban tổ chức thành công.");
    }
}
