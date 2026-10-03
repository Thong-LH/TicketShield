using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.ResaleListings.Commands.ProcessTicketUsedWebhook;

public class OrganizerTicketUsedWebhookRequest
{
    public string TicketCode { get; set; } = string.Empty;
}

public class ProcessTicketUsedWebhookResponse
{
    public Guid EscrowId { get; set; }
    public string EscrowStatus { get; set; } = string.Empty;
}

public record ProcessTicketUsedWebhookCommand(OrganizerTicketUsedWebhookRequest Payload) : IRequest<ApiResponse<ProcessTicketUsedWebhookResponse>>;
