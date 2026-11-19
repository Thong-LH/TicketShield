using System.Text.Json.Serialization;

namespace TicketShield.AntiBot.Gateway.Models;

/// <summary>
/// DTO chứa đầy đủ các chiều đặc trưng trích xuất được ở cả Client và Gateway Server
/// phục vụ cho kịch bản thu thập dữ liệu huấn luyện ML Model v2.0 (US-13.3, ML-13.3.1).
/// </summary>
public record AntiBotDebugVectorDto
{
    // ==========================================
    // 1. Vector 6 Chiều Cốt Lõi (In-Process ONNX)
    // ==========================================
    [JsonPropertyName("time_to_checkout")]
    public float TimeToCheckout { get; init; } = -1.0f;

    [JsonPropertyName("action_interval_std_dev")]
    public float ActionIntervalStdDev { get; init; } = -1.0f;

    [JsonPropertyName("burst_rate_10s")]
    public float BurstRate10s { get; init; } = 1.0f;

    [JsonPropertyName("concurrent_hold")]
    public float ConcurrentHold { get; init; } = 0.0f;

    [JsonPropertyName("is_webdriver")]
    public float IsWebDriver { get; init; } = -1.0f;

    [JsonPropertyName("device_account_overlap")]
    public float DeviceAccountOverlap { get; init; } = -1.0f;

    // ==========================================
    // 2. Client Telemetry Nâng Cao (US-13.2)
    // ==========================================
    [JsonPropertyName("virtual_gpu_detected")]
    public float? VirtualGpuDetected { get; init; }

    [JsonPropertyName("prototype_tampered")]
    public float? PrototypeTampered { get; init; }

    [JsonPropertyName("canvas_tampered")]
    public float? CanvasTampered { get; init; }

    [JsonPropertyName("canvas_hash")]
    public string? CanvasHash { get; init; }

    [JsonPropertyName("webgl_renderer")]
    public string? WebglRenderer { get; init; }

    [JsonPropertyName("webgl_vendor")]
    public string? WebglVendor { get; init; }

    [JsonPropertyName("client_user_agent")]
    public string? ClientUserAgent { get; init; }

    [JsonPropertyName("device_fingerprint")]
    public string? DeviceFingerprint { get; init; }

    [JsonPropertyName("nonce")]
    public string? Nonce { get; init; }

    [JsonPropertyName("client_timestamp")]
    public long? ClientTimestamp { get; init; }

    // ==========================================
    // 3. Network & Protocol Signals (US-13.1, 13.2.4)
    // ==========================================
    [JsonPropertyName("header_user_agent")]
    public string? HeaderUserAgent { get; init; }

    [JsonPropertyName("ua_match_level")]
    public string? UaMatchLevel { get; init; }

    [JsonPropertyName("ua_mismatch_reason")]
    public string? UaMismatchReason { get; init; }

    [JsonPropertyName("ja4_fingerprint")]
    public string? Ja4Fingerprint { get; init; }

    [JsonPropertyName("http2_fingerprint")]
    public string? Http2Fingerprint { get; init; }

    [JsonPropertyName("network_inconsistency_score")]
    public float? NetworkInconsistencyScore { get; init; }

    [JsonPropertyName("network_inconsistency_reason")]
    public string? NetworkInconsistencyReason { get; init; }

    // ==========================================
    // 4. Gateway Decision & Latency
    // ==========================================
    [JsonPropertyName("risk_score")]
    public float RiskScore { get; init; }

    [JsonPropertyName("decision")]
    public string Decision { get; init; } = "Allow";

    [JsonPropertyName("status_code")]
    public int StatusCode { get; init; } = 200;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("execution_duration_ms")]
    public double ExecutionDurationMs { get; init; }

    [JsonPropertyName("is_data_collection_mode")]
    public bool IsDataCollectionMode { get; init; } = true;
}
