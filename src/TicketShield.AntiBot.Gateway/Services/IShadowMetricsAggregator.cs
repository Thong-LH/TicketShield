using TicketShield.AntiBot.Gateway.Models;

namespace TicketShield.AntiBot.Gateway.Services;

/// <summary>
/// Ảnh chụp thống kê thời gian thực (in-memory snapshot) của hệ thống Shadow Mode đối chứng v1 vs v2
/// (Epic US-13.3, Jira SCRUM-382).
/// </summary>
public record ShadowMetricsSnapshot
{
    public long TotalEvaluations { get; init; }
    public long AgreementCount { get; init; }
    public double AgreementRate { get; init; }
    public long RescuedFalsePositives { get; init; }
    public double RescuedFalsePositiveRate { get; init; }
    public long FrictionReduction { get; init; }
    public double FrictionReductionRate { get; init; }
    public long CaughtStealthBots { get; init; }
    public double CaughtStealthBotRate { get; init; }
    public long CaptchaBotElimination { get; init; }
    public double CaptchaBotEliminationRate { get; init; }
    public long V2EvaluationErrors { get; init; }
    public double AverageRiskScoreDelta { get; init; }
    public string ExecutionMode { get; init; } = "Shadow";
    public DateTimeOffset SnapshotTimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, long> CohortMatrix { get; init; } = new Dictionary<string, long>();
}

/// <summary>
/// Giao diện bộ đếm chỉ số đối chứng Shadow Mode thread-safe in-memory:
/// Tách rời hoàn toàn khỏi I/O đĩa để đảm bảo SLA microsecond và dữ liệu không bao giờ bị mất do buffer overflow.
/// </summary>
public interface IShadowMetricsAggregator
{
    void RecordEvaluation(string cohort, bool decisionMatches, double riskScoreDelta, bool isError = false);
    ShadowMetricsSnapshot GetSnapshot();
    void Reset();
}
