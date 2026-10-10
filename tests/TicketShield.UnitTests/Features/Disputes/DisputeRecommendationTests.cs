using TicketShield.Application.Features.Disputes;

namespace TicketShield.UnitTests.Features.Disputes;

public class DisputeRecommendationTests
{
    private static readonly DateTimeOffset Transfer = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Decide_WhenSuccessIsAtOrAfterTransfer_RecommendsRejectFraud()
    {
        var label = DisputeRecommendation.Decide(Transfer, new[]
        {
            Log("A", Scan("A", Transfer, "SUCCESS"))
        });

        Assert.Equal(DisputeRecommendation.RecommendRejectFraud, label);
    }

    [Fact]
    public void Decide_WhenScanIsBeforeTransfer_RecommendsRefund()
    {
        var label = DisputeRecommendation.Decide(Transfer, new[]
        {
            Log("A", Scan("A", Transfer.AddMinutes(-1), "SUCCESS"))
        });

        Assert.Equal(DisputeRecommendation.RecommendRefund, label);
    }

    [Fact]
    public void Decide_WhenGateRejectsAndBuyerNeverEnters_RecommendsRefund()
    {
        var label = DisputeRecommendation.Decide(Transfer, new[]
        {
            Log("A", Scan("A", Transfer.AddMinutes(5), "INVALID_TICKET"), Scan("A", Transfer.AddMinutes(6), "REVOKED"))
        });

        Assert.Equal(DisputeRecommendation.RecommendRefund, label);
    }

    [Fact]
    public void Decide_WhenScanListIsEmpty_ReturnsNoLabel()
    {
        var label = DisputeRecommendation.Decide(Transfer, new[] { Log("A") });

        Assert.Null(label);
    }

    [Fact]
    public void Decide_WhenDuplicateFollowsSuccess_StillRecommendsRejectFraud()
    {
        var label = DisputeRecommendation.Decide(Transfer, new[]
        {
            Log("A", Scan("A", Transfer, "SUCCESS"), Scan("A", Transfer.AddMinutes(2), "DUPLICATE_ENTRY"))
        });

        Assert.Equal(DisputeRecommendation.RecommendRejectFraud, label);
    }

    [Fact]
    public void Decide_WhenEveryBundleTicketEntered_RecommendsRejectFraud()
    {
        var label = DisputeRecommendation.Decide(Transfer, new[]
        {
            Log("A", Scan("A", Transfer.AddMinutes(1), "SUCCESS")),
            Log("B", Scan("B", Transfer.AddMinutes(2), "SUCCESS"))
        });

        Assert.Equal(DisputeRecommendation.RecommendRejectFraud, label);
    }

    [Fact]
    public void Decide_WhenOneBundleTicketIsRejected_RecommendsRefund()
    {
        var label = DisputeRecommendation.Decide(Transfer, new[]
        {
            Log("A", Scan("A", Transfer.AddMinutes(1), "SUCCESS")),
            Log("B", Scan("B", Transfer.AddMinutes(1), "INVALID_TICKET"))
        });

        Assert.Equal(DisputeRecommendation.RecommendRefund, label);
    }

    [Fact]
    public void Decide_WhenOneBundleTicketWasNotScanned_ReturnsNoLabel()
    {
        var label = DisputeRecommendation.Decide(Transfer, new[]
        {
            Log("A", Scan("A", Transfer.AddMinutes(1), "SUCCESS")),
            Log("B")
        });

        Assert.Null(label);
    }

    [Fact]
    public void Decide_WhenTransferTimeIsMissing_ReturnsNoLabel()
    {
        var label = DisputeRecommendation.Decide(null, new[]
        {
            Log("A", Scan("A", Transfer, "SUCCESS"))
        });

        Assert.Null(label);
    }

    [Fact]
    public void Decide_WhenThereIsNoTicket_ReturnsNoLabel()
    {
        Assert.Null(DisputeRecommendation.Decide(Transfer, Array.Empty<TicketGateLog>()));
    }

    private static TicketGateLog Log(string code, params GateScan[] scans) =>
        new() { TicketCode = code, Scans = scans };

    private static GateScan Scan(string code, DateTimeOffset at, string result) =>
        new()
        {
            TicketCode = code,
            ScannedAt = at,
            ScanResult = result,
            GateName = "Cổng A",
            ScannerDeviceId = "dev-a"
        };
}
