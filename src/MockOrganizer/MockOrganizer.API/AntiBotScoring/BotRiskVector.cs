using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MockOrganizer.API.AntiBotScoring;

/// <summary>
/// Vector 6 chiều biểu diễn hành vi của phiên giao dịch (Contract US-8.1 + US-8.2 + US-8.3 - BE-PIPE-8.2.3).
/// Thứ tự các thuộc tính tương ứng chính xác với input tensor [1, 6] chuẩn hóa của mô hình ONNX:
///   [0] time_to_checkout        (giây, Server Telemetry)
///   [1] action_interval_std_dev (ms, Client SDK)
///   [2] burst_rate_10s          (số lượt bấm trong 10s, Server Redis)
///   [3] concurrent_hold         (số vé giữ, Server Redis - không chọc DB)
///   [4] is_webdriver            (0.0 hoặc 1.0, Client SDK)
///   [5] device_account_overlap  (0.0 - 1.0, Server Redis)
/// </summary>
public record BotRiskVector
{
    [JsonPropertyName("time_to_checkout")]
    [Range(-1.0f, 3600.0f, ErrorMessage = "time_to_checkout must be >= -1.0")]
    public float TimeToCheckout { get; init; }

    [JsonPropertyName("action_interval_std_dev")]
    [Range(-1.0f, 10000.0f, ErrorMessage = "action_interval_std_dev must be >= -1.0")]
    public float ActionIntervalStdDev { get; init; }

    [JsonPropertyName("burst_rate_10s")]
    [Range(0.0f, 1000.0f, ErrorMessage = "burst_rate_10s must be >= 0")]
    public float BurstRate10s { get; init; } = 1.0f;

    [JsonPropertyName("concurrent_hold")]
    [Range(0.0f, 1000.0f, ErrorMessage = "concurrent_hold must be >= 0")]
    public float ConcurrentHold { get; init; }

    [JsonPropertyName("is_webdriver")]
    [Range(-1.0f, 1.0f, ErrorMessage = "is_webdriver must be between -1.0 and 1.0")]
    public float IsWebDriver { get; init; }

    [JsonPropertyName("device_account_overlap")]
    [Range(-1.0f, 1.0f, ErrorMessage = "device_account_overlap must be between -1.0 and 1.0")]
    public float DeviceAccountOverlap { get; init; }

    /// <summary>
    /// Thuộc tính dự phòng tương thích ngược (deprecated từ SCRUM-257).
    /// </summary>
    [JsonPropertyName("device_hash_entropy")]
    public float DeviceHashEntropy { get; init; }

    /// <summary>
    /// Chuyển đổi thành mảng float 6 phần tử phục vụ tạo Tensor ONNX.
    /// Thứ tự khớp 100% với Hợp đồng Chuẩn hóa BE-PIPE-8.2.3:
    ///   [0] time_to_checkout
    ///   [1] action_interval_std_dev
    ///   [2] burst_rate_10s
    ///   [3] concurrent_hold
    ///   [4] is_webdriver
    ///   [5] device_account_overlap
    /// </summary>
    public float[] ToArray() =>
    [
        TimeToCheckout,
        ActionIntervalStdDev,
        BurstRate10s,
        ConcurrentHold,
        IsWebDriver,
        DeviceAccountOverlap
    ];
}

/// <summary>
/// Quyết định hành vi theo ngưỡng rủi ro của TicketShield.
/// </summary>
public enum AntiBotDecision
{
    Allow,      // Score < 0.35 (Cho phép thanh toán mượt mà)
    Challenge,  // 0.35 <= Score < 0.75 (Kích hoạt PoW / Turnstile CAPTCHA)
    Block       // Score >= 0.75 (Từ chối giao dịch, chặn bot)
}

/// <summary>
/// Payload phản hồi của API chấm điểm Bot Risk.
/// </summary>
public record AntiBotScoreResponse
{
    [JsonPropertyName("risk_score")]
    public float RiskScore { get; init; }

    [JsonPropertyName("decision")]
    public string Decision { get; init; } = string.Empty;

    [JsonPropertyName("inference_time_ms")]
    public double InferenceTimeMs { get; init; }

    [JsonPropertyName("details")]
    public string Details { get; init; } = string.Empty;
}
