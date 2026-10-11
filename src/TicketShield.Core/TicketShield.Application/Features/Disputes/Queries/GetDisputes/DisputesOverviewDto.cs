namespace TicketShield.Application.Features.Disputes.Queries.GetDisputes;

public class DisputesOverviewDto
{
    public List<DisputeListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int OpenCount { get; set; }
    public int ResolvedCount { get; set; }
    public int RejectedCount { get; set; }
}
