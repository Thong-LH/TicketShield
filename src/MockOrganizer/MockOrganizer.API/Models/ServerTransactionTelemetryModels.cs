using System.Text.Json.Serialization;

namespace MockOrganizer.API.Models;

/// <summary>
/// Kết quả đo đạc thời gian giao dịch, tồn kho giữ chỗ và tần suất Redis (BE-PIPE-8.2.1 + BE-PIPE-8.2.2).
/// </summary>
/// <param name="TimeToCheckoutSeconds">Thời gian từ lúc cấp session token đến khi checkout (giây). -1.0 nếu token không hợp lệ.</param>
/// <param name="ConcurrentHoldCount">Tổng số vé đang ở trạng thái PENDING trong cửa sổ giữ chỗ của tài khoản.</param>
/// <param name="IssuedAtUtc">Thời điểm cấp phát session token.</param>
/// <param name="ReceivedAtUtc">Thời điểm server tiếp nhận request checkout.</param>
/// <param name="ExecutionDurationMs">Thời gian server thực thi phép đo tổng thể (ms) để kiểm chứng tiêu chí SLA &lt; 2ms.</param>
/// <param name="IsSessionValid">Cờ kiểm tra session token có hợp lệ và chữ ký HMAC khớp hay không.</param>
/// <param name="BurstRate10s">Số lượt bấm trong 10s (Redis Sorted Set, tối thiểu 1).</param>
/// <param name="DeviceAccountOverlap">Số tài khoản khác nhau trên thiết bị trong 24h (-1 nếu thiếu device fingerprint).</param>
/// <param name="RedisDurationMs">Thời gian đo Redis Sliding Window (ms) SLA &lt; 1.5ms.</param>
public record ServerTransactionTelemetryResult(
    double TimeToCheckoutSeconds,
    int ConcurrentHoldCount,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    double ExecutionDurationMs,
    bool IsSessionValid,
    int BurstRate10s = 1,
    int DeviceAccountOverlap = -1,
    double RedisDurationMs = 0.0
);

/// <summary>
/// DTO chứa telemetry gửi từ Client SDK (BE-PIPE-8.2.2 + Contract Spec 08).
/// </summary>
public record ClientTelemetryDto
{
    [JsonPropertyName("is_webdriver")]
    public int? IsWebdriver { get; init; }

    [JsonPropertyName("action_interval_std_dev")]
    public float? ActionIntervalStdDev { get; init; }

    [JsonPropertyName("device_fingerprint")]
    public string? DeviceFingerprint { get; init; }

    /// <summary>Mã dùng một lần do Client SDK sinh cho mỗi request (SCRUM-247).</summary>
    [JsonPropertyName("nonce")]
    public string? Nonce { get; init; }

    /// <summary>Giờ gửi request (epoch ms) đã chỉnh theo giờ server (SCRUM-247).</summary>
    [JsonPropertyName("client_timestamp")]
    public long? ClientTimestamp { get; init; }
}

/// <summary>
/// DTO phản hồi khi client yêu cầu cấp session token mới trước khi checkout.
/// </summary>
public record SessionTokenResponse(
    string SessionToken,
    Guid TierId,
    DateTimeOffset IssuedAtUtc,
    int ExpiresInSeconds
);

