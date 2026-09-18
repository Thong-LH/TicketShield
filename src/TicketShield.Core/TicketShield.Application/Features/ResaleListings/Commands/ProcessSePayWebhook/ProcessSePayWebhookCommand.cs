using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;

public class ProcessSePayWebhookCommand : IRequest<ApiResponse<ProcessSePayWebhookResponse>>
{
    public SePayWebhookRequest Payload { get; }

    public ProcessSePayWebhookCommand(SePayWebhookRequest payload)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }
}
