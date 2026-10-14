using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Organizers.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Admin.Organizers.Commands.UpdateOrganizerConnection;

public class UpdateOrganizerConnectionCommandHandler(
    ITicketShieldDbContext dbContext,
    IOrganizerGatewayResolver gatewayResolver)
    : IRequestHandler<UpdateOrganizerConnectionCommand, ApiResponse<AdminOrganizerDto>>
{
    public async Task<ApiResponse<AdminOrganizerDto>> Handle(
        UpdateOrganizerConnectionCommand request,
        CancellationToken ct)
    {
        var organizer = await dbContext.Organizers
            .Include(o => o.Events)
            .FirstOrDefaultAsync(o => o.Id == request.Id, ct);

        if (organizer == null)
        {
            throw new NotFoundException($"Không tìm thấy ban tổ chức với ID '{request.Id}'.");
        }

        // If endpoint changed, invalidate old channel in resolver cache
        if (!string.IsNullOrWhiteSpace(organizer.GrpcEndpoint) &&
            !string.Equals(organizer.GrpcEndpoint, request.GrpcEndpoint?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            gatewayResolver.InvalidateChannel(organizer.GrpcEndpoint);
        }

        organizer.Name = request.Name.Trim();
        organizer.ContactPhone = request.ContactPhone?.Trim();
        organizer.TaxCode = request.TaxCode?.Trim();
        organizer.ContactPerson = request.ContactPerson?.Trim();
        organizer.ApiBaseUrl = request.ApiBaseUrl?.Trim();
        organizer.GrpcEndpoint = request.GrpcEndpoint?.Trim();
        organizer.WebhookUrl = request.WebhookUrl?.Trim();
        organizer.Status = request.Status;
        organizer.ConnectionStatus = request.ConnectionStatus;
        organizer.UpdatedAt = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.WebhookSecretKey))
        {
            organizer.WebhookSecretKey = request.WebhookSecretKey.Trim();
        }

        await dbContext.SaveChangesAsync(ct);

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
            organizer.UpdatedAt
        );

        return ApiResponse<AdminOrganizerDto>.SuccessResponse(dto, "Cập nhật thông tin kết nối ban tổ chức thành công.");
    }
}
