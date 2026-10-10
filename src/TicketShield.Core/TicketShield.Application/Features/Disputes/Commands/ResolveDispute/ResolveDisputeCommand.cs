using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Disputes.Commands.ResolveDispute;

public enum DisputeDecision
{
    Approve,
    Reject
}

public class ResolveDisputeCommand : IRequest<ApiResponse<ResolveDisputeResponse>>
{
    public Guid DisputeId { get; set; }
    public DisputeDecision? Decision { get; set; }
    public string Reason { get; set; } = string.Empty;
}
