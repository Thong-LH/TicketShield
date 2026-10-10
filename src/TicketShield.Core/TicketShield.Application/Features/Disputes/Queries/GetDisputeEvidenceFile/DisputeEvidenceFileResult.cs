namespace TicketShield.Application.Features.Disputes.Queries.GetDisputeEvidenceFile;

public class DisputeEvidenceFileResult
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = string.Empty;
}
