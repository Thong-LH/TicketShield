namespace MockOrganizer.API.AntiBotScoring;

/// <summary>
/// Bản ghi nhật ký phân tích một lượt giao dịch checkout (US-8.4, SCRUM-259).
/// </summary>
public record AntiBotEvaluationLog
{
    public string RequestId { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string BuyerEmail { get; init; } = string.Empty;
    public string? EventId { get; init; }
    public AntiBotDecision Decision { get; init; }
    public float RiskScore { get; init; }
    public double LatencyMs { get; init; }
    public bool IsFailOpen { get; init; }
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Tổng quan phân bổ lưu lượng và số liệu an ninh phục vụ Dashboard BTC (SCRUM-259).
/// </summary>
public record AntiBotAnalyticsSummary
{
    public long TotalEvaluations { get; init; }
    public long AllowCount { get; init; }
    public long ChallengeCount { get; init; }
    public long BlockCount { get; init; }
    public double AllowPercentage => TotalEvaluations > 0 ? Math.Round((double)AllowCount / TotalEvaluations * 100, 1) : 0.0;
    public double ChallengePercentage => TotalEvaluations > 0 ? Math.Round((double)ChallengeCount / TotalEvaluations * 100, 1) : 0.0;
    public double BlockPercentage => TotalEvaluations > 0 ? Math.Round((double)BlockCount / TotalEvaluations * 100, 1) : 0.0;
    public double AverageLatencyMs { get; init; }
    public AntiBotPolicyConfig CurrentPolicy { get; init; } = new();
    public IReadOnlyList<AntiBotEvaluationLog> RecentLogs { get; init; } = Array.Empty<AntiBotEvaluationLog>();
}

public interface IAntiBotAnalyticsCollector
{
    /// <summary>
    /// Ghi nhận kết quả phân tích một lượt checkout.
    /// </summary>
    void RecordEvaluation(AntiBotEvaluationLog log);

    /// <summary>
    /// Lấy báo cáo thống kê phân bổ lưu lượng và các giao dịch gần nhất.
    /// </summary>
    AntiBotAnalyticsSummary GetSummary(string? eventId = null);

    /// <summary>
    /// Đặt lại số liệu thống kê (phục vụ reset phiên demo hoặc test).
    /// </summary>
    void Reset(string? eventId = null);
}
