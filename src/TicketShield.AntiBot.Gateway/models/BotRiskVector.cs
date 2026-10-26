using System.Text.Json.Serialization;

namespace TicketShield.AntiBot.Gateway.Models;

/// <summary>
/// Vector 6 chiều chuẩn hóa theo Hợp đồng BE-PIPE-8.2.3:
/// [0] time_to_checkout (giây, trích xuất từ server timestamp trong SessionToken)
/// [1] action_interval_std_dev (ms, Client SDK, mặc định -1.0 nếu thiếu)
/// [2] burst_rate_10s (số request, Server Redis)
/// [3] concurrent_hold (số vé giữ, Server Redis ZSET atomic Lua script)
/// [4] is_webdriver (0.0 hoặc 1.0, Client SDK, mặc định -1.0)
/// [5] device_account_overlap (số tài khoản, Server Redis, mặc định -1.0)
/// </summary>
public record BotRiskVector
{
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
