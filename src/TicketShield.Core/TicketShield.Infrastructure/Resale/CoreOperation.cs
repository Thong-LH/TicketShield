namespace TicketShield.Infrastructure.Resale;

/// <summary>
/// Trạng thái tác vụ Idempotent (Idempotent Operation) chống gọi trùng lặp và phục hồi lỗi mạng
/// </summary>
public sealed class CoreOperation
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Seller { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string State { get; set; } = "Pending";
    public string? Error { get; set; }
    public string? Step { get; set; }
    public string? LockId { get; set; }
    public ulong LockGeneration { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
}
