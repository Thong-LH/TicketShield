using MediatR;

namespace TicketShield.Application.Features.Disputes.Queries.GetDisputeEvidenceFile;

public class GetDisputeEvidenceFileQuery : IRequest<DisputeEvidenceFileResult>
{
    public Guid DisputeId { get; set; }
    public Guid EvidenceId { get; set; }
}
