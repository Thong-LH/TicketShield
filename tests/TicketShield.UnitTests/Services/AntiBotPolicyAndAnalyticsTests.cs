using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MockOrganizer.API.AntiBotScoring;
using Xunit;

namespace TicketShield.UnitTests.Services;

/// <summary>
/// Unit Tests cho US-8.4 (SCRUM-259, SCRUM-260):
/// Kiểm thử cấu hình ngưỡng động và bộ thu thập số liệu phân bổ lưu lượng.
/// </summary>
public sealed class AntiBotPolicyAndAnalyticsTests
{
    [Fact]
    public void PolicyManager_DefaultValues_Are_035_And_075()
    {
        var manager = new AntiBotPolicyManager(null, NullLogger<AntiBotPolicyManager>.Instance);
        var policy = manager.GetPolicy();

        Assert.Equal(0.35f, policy.AllowThreshold);
        Assert.Equal(0.75f, policy.BlockThreshold);
    }

    [Fact]
    public void PolicyManager_TryUpdatePolicy_WithValidThresholds_Succeeds()
    {
        var manager = new AntiBotPolicyManager(null, NullLogger<AntiBotPolicyManager>.Instance);

        bool success = manager.TryUpdatePolicy(0.20f, 0.80f, out var error, updatedBy: "OrganizerAdmin");

        Assert.True(success);
        Assert.Null(error);

        var updated = manager.GetPolicy();
        Assert.Equal(0.20f, updated.AllowThreshold);
        Assert.Equal(0.80f, updated.BlockThreshold);
        Assert.Equal("OrganizerAdmin", updated.UpdatedBy);
    }

    [Fact]
    public void PolicyManager_TryUpdatePolicy_WithAllowGreaterThanOrEqualToBlock_Fails()
    {
        var manager = new AntiBotPolicyManager(null, NullLogger<AntiBotPolicyManager>.Instance);

        bool success = manager.TryUpdatePolicy(0.80f, 0.70f, out var error);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("nhỏ hơn", error);
    }

    [Fact]
    public void PolicyManager_TryUpdatePolicy_WithOutOfBounds_Fails()
    {
        var manager = new AntiBotPolicyManager(null, NullLogger<AntiBotPolicyManager>.Instance);

        bool failNegative = manager.TryUpdatePolicy(-0.1f, 0.75f, out var err1);
        bool failTooHigh = manager.TryUpdatePolicy(0.35f, 1.05f, out var err2);

        Assert.False(failNegative);
        Assert.NotNull(err1);
        Assert.False(failTooHigh);
        Assert.NotNull(err2);
    }

    [Fact]
    public void PolicyManager_EventSpecificPolicy_OverridesGlobalWithoutAffectingOtherEvents()
    {
        var manager = new AntiBotPolicyManager(null, NullLogger<AntiBotPolicyManager>.Instance);

        // Đặt ngưỡng riêng cho concert Anh Trai (hot event -> siết chặt 0.20 và 0.65)
        manager.TryUpdatePolicy(0.20f, 0.65f, out _, eventId: "anh-trai-concert");

        var eventPolicy = manager.GetPolicy("anh-trai-concert");
        var globalPolicy = manager.GetPolicy();
        var otherPolicy = manager.GetPolicy("workshop-music");

        Assert.Equal(0.20f, eventPolicy.AllowThreshold);
        Assert.Equal(0.65f, eventPolicy.BlockThreshold);

        // Sự kiện khác và global vẫn giữ nguyên mặc định
        Assert.Equal(0.35f, globalPolicy.AllowThreshold);
        Assert.Equal(0.75f, globalPolicy.BlockThreshold);
        Assert.Equal(0.35f, otherPolicy.AllowThreshold);
    }

    [Fact]
    public void AnalyticsCollector_RecordEvaluation_AccumulatesCountsCorrectly()
    {
        var policyManager = new AntiBotPolicyManager(null, NullLogger<AntiBotPolicyManager>.Instance);
        var collector = new AntiBotAnalyticsCollector(policyManager);

        collector.RecordEvaluation(new AntiBotEvaluationLog
        {
            RequestId = "req-1",
            Decision = AntiBotDecision.Allow,
            RiskScore = 0.05f,
            LatencyMs = 0.8
        });

        collector.RecordEvaluation(new AntiBotEvaluationLog
        {
            RequestId = "req-2",
            Decision = AntiBotDecision.Challenge,
            RiskScore = 0.55f,
            LatencyMs = 1.2
        });

        collector.RecordEvaluation(new AntiBotEvaluationLog
        {
            RequestId = "req-3",
            Decision = AntiBotDecision.Block,
            RiskScore = 0.95f,
            LatencyMs = 1.0
        });

        var summary = collector.GetSummary();

        Assert.Equal(3, summary.TotalEvaluations);
        Assert.Equal(1, summary.AllowCount);
        Assert.Equal(1, summary.ChallengeCount);
        Assert.Equal(1, summary.BlockCount);
        Assert.Equal(33.3, summary.AllowPercentage);
        Assert.Equal(33.3, summary.ChallengePercentage);
        Assert.Equal(33.3, summary.BlockPercentage);
        Assert.Equal(1.0, summary.AverageLatencyMs);
        Assert.Equal(3, summary.RecentLogs.Count);
    }

    [Fact]
    public void AnalyticsCollector_RingBuffer_MaintainsRecentLogsAndHandlesReset()
    {
        var policyManager = new AntiBotPolicyManager(null, NullLogger<AntiBotPolicyManager>.Instance);
        var collector = new AntiBotAnalyticsCollector(policyManager);

        for (int i = 1; i <= 60; i++)
        {
            collector.RecordEvaluation(new AntiBotEvaluationLog
            {
                RequestId = $"req-{i}",
                Decision = AntiBotDecision.Allow,
                RiskScore = 0.1f,
                LatencyMs = 0.5
            });
        }

        var summary = collector.GetSummary();
        Assert.Equal(60, summary.TotalEvaluations);
        Assert.Equal(50, summary.RecentLogs.Count); // Take(50) snapshot
        Assert.Equal("req-60", summary.RecentLogs[0].RequestId); // Latest first

        collector.Reset();
        var emptySummary = collector.GetSummary();
        Assert.Equal(0, emptySummary.TotalEvaluations);
        Assert.Empty(emptySummary.RecentLogs);
    }
}
