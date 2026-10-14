using MediatR;
using System.Text.Json.Serialization;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Organizers.Models;

namespace TicketShield.Application.Features.Admin.Organizers.Commands.UpdateOrganizerConnection;

public class UpdateOrganizerConnectionCommand : IRequest<ApiResponse<AdminOrganizerDto>>
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? TaxCode { get; set; }
    public string? ContactPerson { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? GrpcEndpoint { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecretKey { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string ConnectionStatus { get; set; } = "CONNECTED";
}
