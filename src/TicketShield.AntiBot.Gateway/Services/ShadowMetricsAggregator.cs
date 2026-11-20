using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TicketShield.AntiBot.Gateway.Models;

namespace TicketShield.AntiBot.Gateway.Services;

/// <summary>
/// Bộ đếm chỉ số đối chứng in-memory dùng Interlocked nguyên tử, thread-safe tuyệt đối (Jira SCRUM-382):
/// Thu thập dữ liệu theo thời gian thực phục vụ quan sát (Observability) và API giám sát.
/// </summary>
public class ShadowMetricsAggregator : IShadowMetricsAggregator
{
    private readonly IOptions<AntiBotScoringOptions> _options;

    private long _totalEvaluations;
    private long _agreementCount;
    private long _rescuedFalsePositives;   // v1_block_v2_allow (Brave Farbling)
    private long _frictionReduction;        // v1_challenge_v2_allow (Power User)
    private long _caughtStealthBots;        // v1_allow_v2_block (Stealth Bot)
    private long _captchaBotElimination;    // v1_challenge_v2_block (Bot vượt CAPTCHA)
    private long _v2EvaluationErrors;       // Ngoại lệ v2 được cách ly an toàn
    private long _riskScoreDeltaSumBits;    // Tổng delta lưu dưới dạng Int64 bits để cộng atomic

    private readonly ConcurrentDictionary<string, long> _cohortCounters = new(StringComparer.OrdinalIgnoreCase);

    public ShadowMetricsAggregator(IOptions<AntiBotScoringOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Ghi nhận 1 lượt đánh giá đối chứng vào bộ đếm nguyên tử.
    /// </summary>
    public void RecordEvaluation(string cohort, bool decisionMatches, double riskScoreDelta, bool isError = false)
    {
        Interlocked.Increment(ref _totalEvaluations);

        if (isError)
        {
            Interlocked.Increment(ref _v2EvaluationErrors);
        }

        if (decisionMatches)
        {
            Interlocked.Increment(ref _agreementCount);
        }

        string normalizedCohort = (cohort ?? "unknown").ToLowerInvariant();
        switch (normalizedCohort)
        {
            case "v1_block_v2_allow":
                Interlocked.Increment(ref _rescuedFalsePositives);
                break;
            case "v1_challenge_v2_allow":
                Interlocked.Increment(ref _frictionReduction);
                break;
            case "v1_allow_v2_block":
                Interlocked.Increment(ref _caughtStealthBots);
                break;
            case "v1_challenge_v2_block":
                Interlocked.Increment(ref _captchaBotElimination);
                break;
        }

        // Cập nhật ma trận phân phối cohort
        _cohortCounters.AddOrUpdate(normalizedCohort, 1, (_, current) => current + 1);

        // Cộng dồn độ lệch điểm rủi ro bằng CompareExchange lock-free
        if (riskScoreDelta > 0)
        {
            long initialBits, computedBits;
            do
            {
                initialBits = Interlocked.Read(ref _riskScoreDeltaSumBits);
                double currentVal = BitConverter.Int64BitsToDouble(initialBits);
                double updatedVal = currentVal + riskScoreDelta;
                computedBits = BitConverter.DoubleToInt64Bits(updatedVal);
            }
            while (initialBits != Interlocked.CompareExchange(ref _riskScoreDeltaSumBits, computedBits, initialBits));
        }
    }

    /// <summary>
    /// Tạo bản chụp chỉ số thống kê tổng hợp tại thời điểm gọi.
    /// </summary>
    public ShadowMetricsSnapshot GetSnapshot()
    {
        long total = Interlocked.Read(ref _totalEvaluations);
        long agreements = Interlocked.Read(ref _agreementCount);
        long rescuedFp = Interlocked.Read(ref _rescuedFalsePositives);
        long frictionRed = Interlocked.Read(ref _frictionReduction);
        long stealthCaught = Interlocked.Read(ref _caughtStealthBots);
        long captchaBot = Interlocked.Read(ref _captchaBotElimination);
        long errors = Interlocked.Read(ref _v2EvaluationErrors);
        double deltaSum = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _riskScoreDeltaSumBits));

        double totalDouble = total > 0 ? (double)total : 1.0;

        return new ShadowMetricsSnapshot
        {
            TotalEvaluations = total,
            AgreementCount = agreements,
            AgreementRate = total > 0 ? Math.Round((double)agreements / totalDouble, 4) : 0.0,
            RescuedFalsePositives = rescuedFp,
            RescuedFalsePositiveRate = total > 0 ? Math.Round((double)rescuedFp / totalDouble, 4) : 0.0,
            FrictionReduction = frictionRed,
            FrictionReductionRate = total > 0 ? Math.Round((double)frictionRed / totalDouble, 4) : 0.0,
            CaughtStealthBots = stealthCaught,
            CaughtStealthBotRate = total > 0 ? Math.Round((double)stealthCaught / totalDouble, 4) : 0.0,
            CaptchaBotElimination = captchaBot,
            CaptchaBotEliminationRate = total > 0 ? Math.Round((double)captchaBot / totalDouble, 4) : 0.0,
            V2EvaluationErrors = errors,
            AverageRiskScoreDelta = total > 0 ? Math.Round(deltaSum / totalDouble, 4) : 0.0,
            ExecutionMode = _options.Value.ExecutionMode.ToString(),
            SnapshotTimestampUtc = DateTimeOffset.UtcNow,
            CohortMatrix = new Dictionary<string, long>(_cohortCounters)
        };
    }

    /// <summary>
    /// Đặt lại toàn bộ bộ đếm về 0 (phục vụ kiểm thử và reset thống kê định kỳ).
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _totalEvaluations, 0);
        Interlocked.Exchange(ref _agreementCount, 0);
        Interlocked.Exchange(ref _rescuedFalsePositives, 0);
        Interlocked.Exchange(ref _frictionReduction, 0);
        Interlocked.Exchange(ref _caughtStealthBots, 0);
        Interlocked.Exchange(ref _captchaBotElimination, 0);
        Interlocked.Exchange(ref _v2EvaluationErrors, 0);
        Interlocked.Exchange(ref _riskScoreDeltaSumBits, 0);
        _cohortCounters.Clear();
    }
}
