using System.Collections.Concurrent;

namespace MockOrganizer.API.AntiBotScoring;

/// <summary>
/// Bộ thu thập và tổng hợp số liệu an ninh thời gian thực (US-8.4, SCRUM-259).
/// Sử dụng Atomic Counters và Ring Buffer thread-safe để bảo đảm hiệu năng cao (&lt; 0.1ms ghi nhận).
/// </summary>
public class AntiBotAnalyticsCollector : IAntiBotAnalyticsCollector
{
    private const int MaxRecentLogsCapacity = 100;
    private readonly IAntiBotPolicyManager _policyManager;
    private readonly ConcurrentQueue<AntiBotEvaluationLog> _recentLogs = new();
    private readonly object _lock = new();

    private long _totalEvaluations;
    private long _allowCount;
    private long _challengeCount;
    private long _blockCount;
    private double _totalLatencyMs;

    public AntiBotAnalyticsCollector(IAntiBotPolicyManager policyManager)
    {
        _policyManager = policyManager;
    }

    public void RecordEvaluation(AntiBotEvaluationLog log)
    {
        Interlocked.Increment(ref _totalEvaluations);

        switch (log.Decision)
        {
            case AntiBotDecision.Allow:
                Interlocked.Increment(ref _allowCount);
                break;
            case AntiBotDecision.Challenge:
                Interlocked.Increment(ref _challengeCount);
                break;
            case AntiBotDecision.Block:
                Interlocked.Increment(ref _blockCount);
                break;
        }

        lock (_lock)
        {
            _totalLatencyMs += log.LatencyMs;
            _recentLogs.Enqueue(log);

            while (_recentLogs.Count > MaxRecentLogsCapacity)
            {
                _recentLogs.TryDequeue(out _);
            }
        }
    }

    public AntiBotAnalyticsSummary GetSummary(string? eventId = null)
    {
        long total = Interlocked.Read(ref _totalEvaluations);
        long allow = Interlocked.Read(ref _allowCount);
        long challenge = Interlocked.Read(ref _challengeCount);
        long block = Interlocked.Read(ref _blockCount);

        double avgLatency;
        AntiBotEvaluationLog[] snapshot;

        lock (_lock)
        {
            avgLatency = total > 0 ? Math.Round(_totalLatencyMs / total, 2) : 0.0;
            snapshot = _recentLogs.ToArray();
        }

        // Đảo ngược mảng để giao dịch mới nhất nằm ở đầu danh sách
        var logs = snapshot
            .Where(l => string.IsNullOrWhiteSpace(eventId) || string.Equals(l.EventId, eventId, StringComparison.OrdinalIgnoreCase))
            .Reverse()
            .Take(50)
            .ToList();

        return new AntiBotAnalyticsSummary
        {
            TotalEvaluations = total,
            AllowCount = allow,
            ChallengeCount = challenge,
            BlockCount = block,
            AverageLatencyMs = avgLatency,
            CurrentPolicy = _policyManager.GetPolicy(eventId),
            RecentLogs = logs
        };
    }

    public void Reset(string? eventId = null)
    {
        lock (_lock)
        {
            Interlocked.Exchange(ref _totalEvaluations, 0);
            Interlocked.Exchange(ref _allowCount, 0);
            Interlocked.Exchange(ref _challengeCount, 0);
            Interlocked.Exchange(ref _blockCount, 0);
            _totalLatencyMs = 0.0;

            while (_recentLogs.TryDequeue(out _)) { }
        }
    }
}
