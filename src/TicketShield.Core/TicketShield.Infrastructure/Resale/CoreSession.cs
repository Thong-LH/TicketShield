namespace TicketShield.Infrastructure.Resale;

/// <summary>
/// Trạng thái phiên xác thực vé (Verification Session) trong máy trạng thái phân tán (Saga)
/// </summary>
public sealed class CoreSession
{
    public string Id { get; set; } = string.Empty;
    public string Seller { get; set; } = string.Empty;
    public string TicketCode { get; set; } = string.Empty;
    public string State { get; set; } = "RequestPending";
    public string? PendingOperationId { get; set; }
    public string? ChallengeJson { get; set; }
    public string? ReceiptJson { get; set; }
    public Guid? ListingId { get; set; }
    public string? PrivateAccessToken { get; set; }
    public long? Price { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
