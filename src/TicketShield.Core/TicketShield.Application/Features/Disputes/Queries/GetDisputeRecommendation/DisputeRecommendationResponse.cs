using TicketShield.Application.Features.Disputes;

namespace TicketShield.Application.Features.Disputes.Queries.GetDisputeRecommendation;

public class DisputeRecommendationResponse
{
    public string? Recommendation { get; set; }
    public DateTimeOffset? TransferredAt { get; set; }
    public DateTimeOffset? HarvestedAt { get; set; }
    public List<GateScan> Scans { get; set; } = new();
}
