namespace TicketShield.AntiBot.Gateway.Models;

public record AntiBotScoreResponse
{
    public float RiskScore { get; init; }
    public string Decision { get; init; } = "Allow";
    public double InferenceTimeMs { get; init; }
    public string Details { get; init; } = string.Empty;
}

public record AntiBotGuardResult
{
    public AntiBotDecision Decision { get; init; } = AntiBotDecision.Allow;
    public float RiskScore { get; init; } = 0.0f;
    public bool IsFailOpen { get; init; } = false;
    public double ExecutionDurationMs { get; init; }
    public BotRiskVector Vector { get; init; } = null!;
    public string Reason { get; init; } = string.Empty;
}
