namespace TicketShield.Infrastructure.Resale;

/// <summary>
/// Bảng lưu trữ trạng thái phi cấu trúc (Document Store JSONB) của các phiên xác thực vé
/// </summary>
public sealed class CoreResaleRow
{
    public string Id { get; set; } = string.Empty;
    public string Json { get; set; } = "{}";
}
