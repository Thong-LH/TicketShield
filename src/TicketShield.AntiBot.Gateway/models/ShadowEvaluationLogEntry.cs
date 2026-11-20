using System.Text.Json.Serialization;

namespace TicketShield.AntiBot.Gateway.Models;

/// <summary>
/// Định dạng chuẩn của Structured Log Entry (.jsonl) ghi nhận kết quả đánh giá đối chứng Shadow Mode
/// phục vụ đo lường tỷ lệ False Positive Rate (FPR), giảm ma sát và bắt bot (Epic US-13.3, Jira SCRUM-382).
/// </summary>
public record ShadowEvaluationLogEntry
{
    [JsonPropertyName("timestamp_utc")]
    public string TimestampUtc { get; init; } = DateTimeOffset.UtcNow.ToString("o");

    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("execution_mode")]
    public string ExecutionMode { get; init; } = "Shadow";

    [JsonPropertyName("client_ip")]
    public string? ClientIp { get; init; }

    [JsonPropertyName("buyer_email")]
    public string? BuyerEmail { get; init; }

    [JsonPropertyName("device_fingerprint")]
    public string? DeviceFingerprint { get; init; }

    [JsonPropertyName("primary_model")]
    public ModelDecisionSummary PrimaryModel { get; init; } = new();

    [JsonPropertyName("shadow_model")]
    public ModelDecisionSummary? ShadowModel { get; init; }

    [JsonPropertyName("shadow_cohort")]
    public string ShadowCohort { get; init; } = string.Empty;

    [JsonPropertyName("decision_matches")]
    public bool DecisionMatches { get; init; }

    [JsonPropertyName("risk_score_delta")]
    public double RiskScoreDelta { get; init; }

    [JsonPropertyName("is_false_positive_candidate")]
    public bool IsFalsePositiveCandidate { get; init; }

    [JsonPropertyName("is_friction_reduction_candidate")]
    public bool IsFrictionReductionCandidate { get; init; }

    [JsonPropertyName("is_stealth_catch_candidate")]
    public bool IsStealthCatchCandidate { get; init; }

    [JsonPropertyName("features_v2")]
    public ShadowEvaluationFeaturesV2 FeaturesV2 { get; init; } = new();

    [JsonPropertyName("latency_ms")]
    public ShadowEvaluationLatencySummary LatencyMs { get; init; } = new();

    /// <summary>
    /// Factory tạo log entry từ ngữ cảnh checkout và kết quả đánh giá của 2 mô hình.
    /// </summary>
    public static ShadowEvaluationLogEntry Create(
        string requestId,
        AntiBotExecutionMode executionMode,
        CheckoutTelemetryContext context,
        AntiBotScoreResponse primary,
        AntiBotScoreResponse? shadow,
        string cohort,
        double evaluationMs,
        double totalGuardMs,
        DateTimeOffset? timestamp = null)
    {
        bool decisionMatches = shadow != null && string.Equals(primary.Decision, shadow.Decision, StringComparison.OrdinalIgnoreCase);
        double delta = shadow != null
            ? Math.Round(Math.Abs(primary.RiskScore - shadow.RiskScore), 4)
            : 0.0;

        string normalizedCohort = (cohort ?? string.Empty).ToLowerInvariant();
        bool isFp = normalizedCohort == "v1_block_v2_allow";
        bool isFriction = normalizedCohort == "v1_challenge_v2_allow";
        bool isStealth = normalizedCohort == "v1_allow_v2_block" || normalizedCohort == "v1_challenge_v2_block";

        return new ShadowEvaluationLogEntry
        {
            TimestampUtc = (timestamp ?? DateTimeOffset.UtcNow).ToString("o"),
            RequestId = requestId,
            ExecutionMode = executionMode.ToString(),
            ClientIp = context.ClientIp ?? "unknown",
            BuyerEmail = context.BuyerEmail,
            DeviceFingerprint = context.DeviceFingerprint,
            PrimaryModel = new ModelDecisionSummary
            {
                Version = executionMode == AntiBotExecutionMode.Enforce ? "2.0.0" : "1.0.0",
                RiskScore = Math.Round(primary.RiskScore, 4),
                Decision = primary.Decision
            },
            ShadowModel = shadow != null ? new ModelDecisionSummary
            {
                Version = executionMode == AntiBotExecutionMode.Enforce ? "1.0.0" : "2.0.0",
                RiskScore = Math.Round(shadow.RiskScore, 4),
                Decision = shadow.Decision
            } : null,
            ShadowCohort = cohort ?? string.Empty,
            DecisionMatches = decisionMatches,
            RiskScoreDelta = delta,
            IsFalsePositiveCandidate = isFp,
            IsFrictionReductionCandidate = isFriction,
            IsStealthCatchCandidate = isStealth,
            FeaturesV2 = new ShadowEvaluationFeaturesV2
            {
                IsWebDriver = context.IsWebDriver,
                VirtualGpuDetected = context.VirtualGpuDetected,
                PrototypeTampered = context.PrototypeTampered,
                CanvasTampered = context.CanvasTampered,
                ActionIntervalStdDev = context.ActionIntervalStdDev,
                UaMatchLevel = context.UaMatchLevel,
                NetworkInconsistencyScore = context.NetworkInconsistencyScore,
                TimeToCheckout = context.TimeToCheckout,
                BurstRate10s = context.BurstRate10s,
                ConcurrentHold = context.ConcurrentHold,
                DeviceAccountOverlap = context.DeviceAccountOverlap
            },
            LatencyMs = new ShadowEvaluationLatencySummary
            {
                EvaluationMs = Math.Round(evaluationMs, 4),
                TotalGuardMs = Math.Round(totalGuardMs, 3)
            }
        };
    }
}

public record ModelDecisionSummary
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";

    [JsonPropertyName("risk_score")]
    public double RiskScore { get; init; }

    [JsonPropertyName("decision")]
    public string Decision { get; init; } = "Allow";
}

public record ShadowEvaluationFeaturesV2
{
    [JsonPropertyName("is_webdriver")]
    public float IsWebDriver { get; init; }

    [JsonPropertyName("virtual_gpu_detected")]
    public float VirtualGpuDetected { get; init; }

    [JsonPropertyName("prototype_tampered")]
    public float PrototypeTampered { get; init; }

    [JsonPropertyName("canvas_tampered")]
    public float CanvasTampered { get; init; }

    [JsonPropertyName("action_interval_std_dev")]
    public float ActionIntervalStdDev { get; init; }

    [JsonPropertyName("ua_match_level")]
    public float UaMatchLevel { get; init; }

    [JsonPropertyName("network_inconsistency_score")]
    public float NetworkInconsistencyScore { get; init; }

    [JsonPropertyName("time_to_checkout")]
    public float TimeToCheckout { get; init; }

    [JsonPropertyName("burst_rate_10s")]
    public float BurstRate10s { get; init; }

    [JsonPropertyName("concurrent_hold")]
    public float ConcurrentHold { get; init; }

    [JsonPropertyName("device_account_overlap")]
    public float DeviceAccountOverlap { get; init; }
}

public record ShadowEvaluationLatencySummary
{
    [JsonPropertyName("evaluation_ms")]
    public double EvaluationMs { get; init; }

    [JsonPropertyName("total_guard_ms")]
    public double TotalGuardMs { get; init; }
}
