namespace MockOrganizer.API.Services;

/// <summary>
/// Kết quả đo đạc telemetry tần suất và thiết bị qua Redis Sliding Window (BE-PIPE-8.2.2).
/// </summary>
/// <param name="BurstRate10s">Số request trong cửa sổ trượt 10 giây (tính cả request hiện tại, tối thiểu 1).</param>
/// <param name="DeviceAccountOverlap">Số tài khoản duy nhất thao tác trên thiết bị trong 24h (-1 nếu thiếu device_fingerprint).</param>
/// <param name="ExecutionDurationMs">Thời gian thực thi của phép đo Redis (ms) để kiểm chứng SLA &lt; 1.5ms.</param>
/// <param name="IsRedisAvailable">Cờ xác định kết quả đo từ Redis thật hay từ In-Memory Fallback.</param>
public record RedisSlidingWindowTelemetryResult(
    int BurstRate10s,
    int DeviceAccountOverlap,
    double ExecutionDurationMs,
    bool IsRedisAvailable
);

public interface IRedisSlidingWindowService
{
    /// <summary>
    /// Đếm số request giữ chỗ/checkout trong cửa sổ trượt 10 giây (burst_rate_10s).
    /// Khách chưa đăng nhập: Max(IP, DeviceFingerprint).
    /// Khách đã điền email: Max(IP, DeviceFingerprint, Account).
    /// </summary>
    Task<int> RecordAndGetBurstRateAsync(
        string? clientIp,
        string? deviceFingerprint,
        string? account,
        CancellationToken ct = default);

    /// <summary>
    /// Đếm số lượng định danh tài khoản khác nhau thao tác trên cùng một device_fingerprint trong 24 giờ (device_account_overlap).
    /// Trả về -1 nếu thiếu device_fingerprint theo Spec 08.
    /// SLA &lt; 1.5ms.
    /// </summary>
    Task<int> RecordAndGetDeviceAccountOverlapAsync(
        string? deviceFingerprint,
        string? account,
        CancellationToken ct = default);

    /// <summary>
    /// Thực hiện đồng thời cả hai phép đo sliding window và đo đạc thời gian thực thi (SLA &lt; 1.5ms).
    /// </summary>
    Task<RedisSlidingWindowTelemetryResult> MeasureSlidingWindowTelemetryAsync(
        string? clientIp,
        string? deviceFingerprint,
        string? account,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy số lượng vé đang được giữ đồng thời (concurrent_hold) của tài khoản/email (Server Redis - không query DB).
    /// Trả về 0 nếu không có hoặc buyerEmail null/empty.
    /// </summary>
    Task<int> GetConcurrentHoldAsync(string? buyerEmail, CancellationToken ct = default);

    /// <summary>
    /// Ghi nhận/tăng số lượng vé đang giữ vào Redis với TTL (mặc định 10 phút) khi checkout thành công.
    /// </summary>
    Task RecordHoldAsync(string? buyerEmail, int quantity, TimeSpan? holdWindow = null, CancellationToken ct = default);

    /// <summary>
    /// Giải phóng số lượng vé đang giữ khi đơn hàng bị hủy hoặc hoàn tất thanh toán.
    /// </summary>
    Task ReleaseHoldAsync(string? buyerEmail, int quantity, CancellationToken ct = default);
}
