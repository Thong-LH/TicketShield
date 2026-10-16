using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MockOrganizer.API.AntiBotScoring;

/// <summary>
/// Vector 6 chiều biểu diễn hành vi của phiên giao dịch (Contract US-8.1 + US-8.2 + US-8.3).
/// Thứ tự các thuộc tính tương ứng chính xác với input tensor [1, 6] của mô hình ONNX:
///   [0] action_interval_std_dev (ms, Client SDK)
///   [1] is_webdriver            (0.0 hoặc 1.0, Client SDK)
///   [2] device_hash_entropy     (0.0 - 1.0, Client SDK)
///   [3] time_to_checkout        (giây, Server Telemetry)
///   [4] burst_rate_10s          (số lượt bấm trong 10s, Server Redis)
///   [5] device_account_overlap  (0.0 - 1.0, Server Redis)
/// </summary>
public record BotRiskVector
{
    [JsonPropertyName("action_interval_std_dev")]
    [Range(0, 10000, ErrorMessage = "action_interval_std_dev must be >= 0")]
    public float ActionIntervalStdDev { get; init; }

    [JsonPropertyName("is_webdriver")]
    [Range(0.0f, 1.0f, ErrorMessage = "is_webdriver must be 0.0 or 1.0")]
    public float IsWebDriver { get; init; }

    [JsonPropertyName("device_hash_entropy")]
    [Range(0.0f, 1.0f, ErrorMessage = "device_hash_entropy must be between 0.0 and 1.0")]
    public float DeviceHashEntropy { get; init; }

    [JsonPropertyName("time_to_checkout")]
    [Range(0.0f, 3600.0f, ErrorMessage = "time_to_checkout must be >= 0")]
    public float TimeToCheckout { get; init; }

    [JsonPropertyName("burst_rate_10s")]
    [Range(0.0f, 1000.0f, ErrorMessage = "burst_rate_10s must be >= 0")]
    public float BurstRate10s { get; init; }

    [JsonPropertyName("device_account_overlap")]
    [Range(0.0f, 1.0f, ErrorMessage = "device_account_overlap must be between 0.0 and 1.0")]
    public float DeviceAccountOverlap { get; init; }

    /// <summary>
    /// Chuyển đổi thành mảng float 6 phần tử phục vụ tạo Tensor ONNX.
    /// </summary>
    public float[] ToArray() =>
    [
        ActionIntervalStdDev,
        IsWebDriver,
        DeviceHashEntropy,
        TimeToCheckout,
        BurstRate10s,
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
