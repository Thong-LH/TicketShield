using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Disputes.Commands.AddDisputeEvidence;

public class AddDisputeEvidenceCommand : IRequest<ApiResponse<AddDisputeEvidenceResponse>>
{
    public Guid DisputeId { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string? ContentType { get; set; }
}
