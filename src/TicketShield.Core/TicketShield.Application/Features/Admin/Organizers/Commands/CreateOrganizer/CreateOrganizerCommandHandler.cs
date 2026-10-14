using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Organizers.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Admin.Organizers.Commands.CreateOrganizer;

public class CreateOrganizerCommandHandler(ITicketShieldDbContext dbContext)
    : IRequestHandler<CreateOrganizerCommand, ApiResponse<AdminOrganizerDto>>
{
    public async Task<ApiResponse<AdminOrganizerDto>> Handle(
        CreateOrganizerCommand request,
        CancellationToken ct)
    {
        var emailExists = await dbContext.Organizers
            .AnyAsync(o => o.OfficialEmail == request.OfficialEmail, ct);

        if (emailExists)
        {
            throw new BadRequestException($"Ban tổ chức với email '{request.OfficialEmail}' đã tồn tại.");
        }

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            OfficialEmail = request.OfficialEmail.Trim().ToLowerInvariant(),
            ContactPhone = request.ContactPhone?.Trim(),
            TaxCode = request.TaxCode?.Trim(),
            ContactPerson = request.ContactPerson?.Trim(),
            Status = "DRAFT",
            ApiKeyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")))).ToLowerInvariant(),
            ApiBaseUrl = request.ApiBaseUrl?.Trim(),
            GrpcEndpoint = request.GrpcEndpoint?.Trim(),
            WebhookUrl = request.WebhookUrl?.Trim(),
            WebhookSecretKey = !string.IsNullOrWhiteSpace(request.WebhookSecretKey)
                ? request.WebhookSecretKey.Trim()
                : "whsec_" + Guid.NewGuid().ToString("N")[..16],
            ConnectionStatus = !string.IsNullOrWhiteSpace(request.GrpcEndpoint) || !string.IsNullOrWhiteSpace(request.ApiBaseUrl)
                ? "CONNECTED"
                : "PENDING"
        };

        await dbContext.Organizers.AddAsync(organizer, ct);
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
            0,
            organizer.LastPingAt,
            organizer.LastPingLatencyMs,
            organizer.LastCatalogSyncedAt,
            organizer.CreatedAt,
            organizer.UpdatedAt
        );

        return ApiResponse<AdminOrganizerDto>.SuccessResponse(dto, "Khởi tạo ban tổ chức đối tác thành công.");
    }
}
