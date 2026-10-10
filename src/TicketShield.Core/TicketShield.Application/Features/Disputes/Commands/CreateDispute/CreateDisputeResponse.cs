namespace TicketShield.Application.Features.Disputes.Commands.CreateDispute;

public class CreateDisputeResponse
{
    public Guid DisputeId { get; set; }
    public string DisputeCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
