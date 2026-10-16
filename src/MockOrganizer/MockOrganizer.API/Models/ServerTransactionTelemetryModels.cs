namespace MockOrganizer.API.Models;

/// <summary>
/// Kết quả đo đạc thời gian giao dịch và tra cứu tồn kho giữ chỗ (BE-PIPE-8.2.1).
/// </summary>
/// <param name="TimeToCheckoutSeconds">Thời gian từ lúc cấp session token đến khi checkout (giây). -1.0 nếu token không hợp lệ.</param>
/// <param name="ConcurrentHoldCount">Tổng số vé đang ở trạng thái PENDING trong cửa sổ giữ chỗ của tài khoản.</param>
/// <param name="IssuedAtUtc">Thời điểm cấp phát session token.</param>
/// <param name="ReceivedAtUtc">Thời điểm server tiếp nhận request checkout.</param>
/// <param name="ExecutionDurationMs">Thời gian server thực thi phép đo (ms) để kiểm chứng tiêu chí SLA &lt; 2ms.</param>
/// <param name="IsSessionValid">Cờ kiểm tra session token có hợp lệ và chữ ký HMAC khớp hay không.</param>
public record ServerTransactionTelemetryResult(
    double TimeToCheckoutSeconds,
    int ConcurrentHoldCount,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    double ExecutionDurationMs,
    bool IsSessionValid
);

/// <summary>
/// DTO phản hồi khi client yêu cầu cấp session token mới trước khi checkout.
/// </summary>
public record SessionTokenResponse(
    string SessionToken,
    Guid TierId,
    DateTimeOffset IssuedAtUtc,
    int ExpiresInSeconds
);
