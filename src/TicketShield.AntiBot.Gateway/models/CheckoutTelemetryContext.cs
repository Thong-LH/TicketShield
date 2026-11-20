namespace TicketShield.AntiBot.Gateway.Models;

/// <summary>
/// Ngữ cảnh tín hiệu telemetry toàn diện 3 tầng trích xuất từ Checkout Request (Epic US-13.3, Jira SCRUM-382):
/// Đảm bảo tính thông suốt 11 chiều đặc trưng từ Middleware truyền xuyên suốt vào Service.
/// </summary>
public record CheckoutTelemetryContext
{
    // === TẦNG 1: BROWSER / CLIENT TELEMETRY (5 chiều) ===
    public float IsWebDriver { get; init; } = -1.0f;
    public float VirtualGpuDetected { get; init; } = -1.0f;
    public float PrototypeTampered { get; init; } = -1.0f;
    public float CanvasTampered { get; init; } = -1.0f;
    public float ActionIntervalStdDev { get; init; } = -1.0f;

    // === TẦNG 2: NETWORK / PROTOCOL CONSISTENCY (2 chiều) ===
    public float UaMatchLevel { get; init; } = -1.0f;
    public float NetworkInconsistencyScore { get; init; } = 0.0f;

    // === TẦNG 3: BUSINESS / BEHAVIORAL DYNAMICS (4 chiều) ===
    public float TimeToCheckout { get; init; } = -1.0f;
    public float BurstRate10s { get; init; } = 1.0f;
    public float ConcurrentHold { get; init; } = 0.0f;
    public float DeviceAccountOverlap { get; init; } = -1.0f;

    // === METADATA & IDENTITY SIGNALS ===
    public string? ClientIp { get; init; }
    public string? BuyerEmail { get; init; }
    public string? SessionToken { get; init; }
    public string? DeviceFingerprint { get; init; }
    public string? ClientNonce { get; init; }
    public long? ClientTimestamp { get; init; }
    public string? ClientUserAgent { get; init; }
    public string? HeaderUserAgent { get; init; }
    public string? CanvasHash { get; init; }
    public string? WebglRenderer { get; init; }
    public string? WebglVendor { get; init; }
    public string? Ja4Fingerprint { get; init; }
    public string? Http2Fingerprint { get; init; }

    /// <summary>
    /// Chuyển đổi thành vector v1 6 chiều cũ.
    /// </summary>
    public BotRiskVector ToRiskVectorV1() => new()
    {
        TimeToCheckout = TimeToCheckout,
        ActionIntervalStdDev = ActionIntervalStdDev,
        BurstRate10s = BurstRate10s,
        ConcurrentHold = ConcurrentHold,
        IsWebDriver = IsWebDriver,
        DeviceAccountOverlap = DeviceAccountOverlap
    };

    /// <summary>
    /// Chuyển đổi thành vector v2 11 chiều mới.
    /// </summary>
    public BotRiskVectorV2 ToRiskVectorV2() => new()
    {
        IsWebDriver = IsWebDriver,
        VirtualGpuDetected = VirtualGpuDetected,
        PrototypeTampered = PrototypeTampered,
        CanvasTampered = CanvasTampered,
        ActionIntervalStdDev = ActionIntervalStdDev,
        UaMatchLevel = UaMatchLevel,
        NetworkInconsistencyScore = NetworkInconsistencyScore,
        TimeToCheckout = TimeToCheckout,
        BurstRate10s = BurstRate10s,
        ConcurrentHold = ConcurrentHold,
        DeviceAccountOverlap = DeviceAccountOverlap
    };
}
