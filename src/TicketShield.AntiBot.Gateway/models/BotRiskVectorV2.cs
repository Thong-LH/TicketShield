using System.Text.Json.Serialization;

namespace TicketShield.AntiBot.Gateway.Models;

/// <summary>
/// Vector 11 chiều chuẩn hóa 3 tầng theo Hợp đồng AI Model v2.0 (Epic US-13.3, Jira SCRUM-381):
/// 
/// [Tầng 1: Client / Browser Telemetry - 5 chiều]
/// [0] is_webdriver: 0.0 (Sạch), 1.0 (Automation), mặc định -1.0
/// [1] virtual_gpu_detected: 0.0 (GPU thật), 1.0 (SwiftShader/llvmpipe), mặc định -1.0
/// [2] prototype_tampered: 0.0 (Sạch), 1.0 (Monkey-patch evasion), mặc định -1.0
/// [3] canvas_tampered: 0.0 (Sạch), 1.0 (Brave Farbling/Canvas Bot), mặc định -1.0
/// [4] action_interval_std_dev: Độ lệch chuẩn nhịp bấm Fitts (ms), mặc định -1.0
/// 
/// [Tầng 2: Network / Protocol Consistency - 2 chiều]
/// [5] ua_match_level: 0.0 (Exact), 0.5 (Minor), 1.0 (Blatant), mặc định -1.0
/// [6] network_inconsistency_score: Điểm bất nhất JA4 TLS / HTTP2 (0.0 đến 1.0), mặc định 0.0
/// 
/// [Tầng 3: Business / Behavioral Dynamics - 4 chiều]
/// [7] time_to_checkout: Thời gian từ lúc mở trang đến checkout (giây), mặc định -1.0
/// [8] burst_rate_10s: Số request trong 10 giây (Redis sliding window), mặc định 1.0
/// [9] concurrent_hold: Số vé giữ đồng thời (Redis atomic hold), mặc định 0.0
/// [10] device_account_overlap: Tỷ lệ dùng chung thiết bị nhiều nick (0.0 đến 1.0), mặc định -1.0
/// </summary>
public record BotRiskVectorV2
{
    // Tầng 1: Browser / Client Telemetry (5 chiều)
    [JsonPropertyName("is_webdriver")]
    public float IsWebDriver { get; init; } = -1.0f;

    [JsonPropertyName("virtual_gpu_detected")]
    public float VirtualGpuDetected { get; init; } = -1.0f;

    [JsonPropertyName("prototype_tampered")]
    public float PrototypeTampered { get; init; } = -1.0f;

    [JsonPropertyName("canvas_tampered")]
    public float CanvasTampered { get; init; } = -1.0f;

    [JsonPropertyName("action_interval_std_dev")]
    public float ActionIntervalStdDev { get; init; } = -1.0f;

    // Tầng 2: Network / Protocol Consistency (2 chiều)
    [JsonPropertyName("ua_match_level")]
    public float UaMatchLevel { get; init; } = -1.0f;

    [JsonPropertyName("network_inconsistency_score")]
    public float NetworkInconsistencyScore { get; init; } = 0.0f;

    // Tầng 3: Business / Behavioral Dynamics (4 chiều)
    [JsonPropertyName("time_to_checkout")]
    public float TimeToCheckout { get; init; } = -1.0f;

    [JsonPropertyName("burst_rate_10s")]
    public float BurstRate10s { get; init; } = 1.0f;

    [JsonPropertyName("concurrent_hold")]
    public float ConcurrentHold { get; init; } = 0.0f;

    [JsonPropertyName("device_account_overlap")]
    public float DeviceAccountOverlap { get; init; } = -1.0f;

    /// <summary>
    /// Xuất mảng 11 phần tử float theo đúng thứ tự khế ước ONNX metadata.
    /// </summary>
    public float[] ToArray() =>
    [
        IsWebDriver,
        VirtualGpuDetected,
        PrototypeTampered,
        CanvasTampered,
        ActionIntervalStdDev,
        UaMatchLevel,
        NetworkInconsistencyScore,
        TimeToCheckout,
        BurstRate10s,
        ConcurrentHold,
        DeviceAccountOverlap
    ];

    /// <summary>
    /// Chuyển đổi từ vector v1 cũ sang vector v2 với các đặc trưng tầng 1 & 2 bổ trợ.
    /// </summary>
    public static BotRiskVectorV2 FromV1(
        BotRiskVector v1,
        float virtualGpuDetected = 0.0f,
        float prototypeTampered = 0.0f,
        float canvasTampered = 0.0f,
        float uaMatchLevel = 0.0f,
        float networkInconsistencyScore = 0.0f)
    {
        return new BotRiskVectorV2
        {
            IsWebDriver = v1.IsWebDriver,
            VirtualGpuDetected = virtualGpuDetected,
            PrototypeTampered = prototypeTampered,
            CanvasTampered = canvasTampered,
            ActionIntervalStdDev = v1.ActionIntervalStdDev,
            UaMatchLevel = uaMatchLevel,
            NetworkInconsistencyScore = networkInconsistencyScore,
            TimeToCheckout = v1.TimeToCheckout,
            BurstRate10s = v1.BurstRate10s,
            ConcurrentHold = v1.ConcurrentHold,
            DeviceAccountOverlap = v1.DeviceAccountOverlap
        };
    }
}
