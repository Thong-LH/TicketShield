using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Organizers.Models;

namespace TicketShield.Application.Features.Admin.Organizers.Commands.CreateOrganizer;

public class CreateOrganizerCommand : IRequest<ApiResponse<AdminOrganizerDto>>
{
    public string Name { get; set; } = string.Empty;
    public string OfficialEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? TaxCode { get; set; }
    public string? ContactPerson { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? GrpcEndpoint { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecretKey { get; set; }
}
