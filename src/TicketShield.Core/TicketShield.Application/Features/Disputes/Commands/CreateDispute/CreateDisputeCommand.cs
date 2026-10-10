using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Disputes.Commands.CreateDispute;

public class CreateDisputeCommand : IRequest<ApiResponse<CreateDisputeResponse>>
{
    public Guid EscrowId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DisputeReasonCode ReasonCode { get; set; } = DisputeReasonCode.TicketInvalid;
}

