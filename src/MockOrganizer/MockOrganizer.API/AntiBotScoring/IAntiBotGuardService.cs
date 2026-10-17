using MockOrganizer.API.Controllers;
using MockOrganizer.API.Models;

namespace MockOrganizer.API.AntiBotScoring;

public record AntiBotGuardResult
{
    public AntiBotDecision Decision { get; init; } = AntiBotDecision.Allow;
    public float RiskScore { get; init; } = 0.0f;
    public bool IsFailOpen { get; init; } = false;
    public double ExecutionDurationMs { get; init; }
    public BotRiskVector Vector { get; init; } = null!;
    public ServerTransactionTelemetryResult? Telemetry { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public interface IAntiBotGuardService
{
    /// <summary>
    /// Thu thập 6D Feature Vector và thực thi suy luận Anti-Bot với 20ms Fail-Open Fallback Guard.
    /// SLA &lt;= 20ms. Nếu Redis hoặc ONNX Runtime vượt 20ms hoặc gặp lỗi, tự động Fail-Open với Allow và RiskScore = 0.0f.
    /// </summary>
    Task<AntiBotGuardResult> EvaluateCheckoutGuardAsync(
        CheckoutRequest request,
        string? clientIp,
        CancellationToken ct = default);
}
