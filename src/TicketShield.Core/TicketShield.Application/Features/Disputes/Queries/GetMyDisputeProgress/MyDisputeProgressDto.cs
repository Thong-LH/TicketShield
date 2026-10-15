using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Disputes.Queries.GetMyDisputeProgress;

// Chỉ có field người mua/người bán được thấy. Gợi ý tự động, log cổng thô,
// ghi chú và người phân xử cố ý không có ở đây để không thể lộ ra ngoài.
public class MyDisputeProgressDto
{
    public Guid DisputeId { get; set; }
    public string DisputeCode { get; set; } = string.Empty;
    public string ViewerRole { get; set; } = string.Empty;
    public DisputeReasonCode ReasonCode { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? Description { get; set; }
    public List<DisputeTimelineStepDto> Timeline { get; set; } = new();
    public string? Outcome { get; set; }
    public string? ResolutionMessage { get; set; }
    public DisputeMoneyDto? Money { get; set; }
}

public class DisputeTimelineStepDto
{
    public string Step { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? At { get; set; }
}

public class DisputeMoneyDto
{
    public string Kind { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ProcessedAt { get; set; }
}
