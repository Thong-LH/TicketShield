using TicketShield.Application.Features.Disputes.Queries.GetMyDisputeProgress;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.UnitTests.Features.Disputes;

public class MyDisputeProgressMapperTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Harvested = Created.AddMinutes(10);
    private static readonly DateTimeOffset Resolved = Created.AddHours(3);

    [Fact]
    public void Build_WhenGateLogCheckIsRunning_MarksStepTwoCurrent()
    {
        var dispute = NewDispute();

        var dto = MyDisputeProgressMapper.Build(dispute, DisputeViewerRole.Buyer);

        AssertTimeline(dto, ("DONE", Created), ("CURRENT", null), ("PENDING", null));
        Assert.Equal("OPEN", dto.Status);
        Assert.Null(dto.Outcome);
        Assert.Null(dto.ResolutionMessage);
        Assert.Null(dto.Money);
    }

    [Fact]
    public void Build_WhenWaitingForStaff_MarksStepThreeCurrent()
    {
        var dispute = NewDispute(harvestedAt: Harvested);

        var dto = MyDisputeProgressMapper.Build(dispute, DisputeViewerRole.Buyer);

        AssertTimeline(dto, ("DONE", Created), ("DONE", Harvested), ("CURRENT", null));
    }

    [Fact]
    public void Build_WhenUnderReview_IsShownAsOpen()
    {
        var dispute = NewDispute(harvestedAt: Harvested, status: DisputeStatus.UnderReview);

        var dto = MyDisputeProgressMapper.Build(dispute, DisputeViewerRole.Buyer);

        Assert.Equal("OPEN", dto.Status);
        AssertTimeline(dto, ("DONE", Created), ("DONE", Harvested), ("CURRENT", null));
    }

    [Fact]
    public void Build_WhenDecided_MarksAllStepsDone()
    {
        var dispute = NewDispute(harvestedAt: Harvested, resolution: DisputeResolution.RefundBuyer);

        var dto = MyDisputeProgressMapper.Build(dispute, DisputeViewerRole.Buyer);

        AssertTimeline(dto, ("DONE", Created), ("DONE", Harvested), ("DONE", Resolved));
        Assert.Equal("RESOLVED", dto.Status);
        Assert.Equal("REFUND_BUYER", dto.Outcome);
    }

    [Fact]
    public void Build_WhenDecidedBeforeGateLogCheck_SkipsStepTwo()
    {
        var dispute = NewDispute(resolution: DisputeResolution.ReleaseSeller);

        var dto = MyDisputeProgressMapper.Build(dispute, DisputeViewerRole.Seller);

        AssertTimeline(dto, ("DONE", Created), ("SKIPPED", null), ("DONE", Resolved));
        Assert.Equal("REJECTED", dto.Status);
        Assert.Equal("RELEASE_SELLER", dto.Outcome);
    }

    [Theory]
    [InlineData(DisputeResolution.RefundBuyer, DisputeViewerRole.Buyer, MyDisputeProgressMapper.BuyerRefundedMessage)]
    [InlineData(DisputeResolution.ReleaseSeller, DisputeViewerRole.Buyer, MyDisputeProgressMapper.BuyerRejectedMessage)]
    [InlineData(DisputeResolution.RefundBuyer, DisputeViewerRole.Seller, MyDisputeProgressMapper.SellerRefundedMessage)]
    [InlineData(DisputeResolution.ReleaseSeller, DisputeViewerRole.Seller, MyDisputeProgressMapper.SellerRejectedMessage)]
    public void Build_WhenDecided_PicksPlainMessageForViewer(DisputeResolution resolution, DisputeViewerRole viewer, string expected)
    {
        var dispute = NewDispute(harvestedAt: Harvested, resolution: resolution);
        dispute.AdminNotes = "Bác bỏ khiếu nại và giải ngân Escrow do log cổng BTC xác nhận vé đã quét.";

        var dto = MyDisputeProgressMapper.Build(dispute, viewer);

        Assert.Equal(expected, dto.ResolutionMessage);
        Assert.DoesNotContain("escrow", dto.ResolutionMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BTC", dto.ResolutionMessage!);
        Assert.DoesNotContain("log", dto.ResolutionMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WhenBuyerIsRefunded_ReturnsApprovedRefund()
    {
        var dispute = NewDispute(harvestedAt: Harvested, resolution: DisputeResolution.RefundBuyer);
        dispute.RefundAmount = 1_550_000m;

        var dto = MyDisputeProgressMapper.Build(dispute, DisputeViewerRole.Buyer);

        Assert.NotNull(dto.Money);
        Assert.Equal("REFUND", dto.Money!.Kind);
        Assert.Equal(1_550_000m, dto.Money.Amount);
        Assert.Equal("APPROVED", dto.Money.Status);
        Assert.Null(dto.Money.ProcessedAt);
    }

    [Fact]
    public void Build_WhenSellerPayoutIsProcessing_ReturnsRealPayoutStatus()
    {
        var dispute = NewDispute(harvestedAt: Harvested, resolution: DisputeResolution.ReleaseSeller);
        dispute.Escrow.PayoutTransaction = new PayoutTransaction
        {
            Id = Guid.NewGuid(),
            EscrowId = dispute.EscrowId,
            Amount = 1_400_000m,
            Status = PayoutStatus.Processing
        };

        var dto = MyDisputeProgressMapper.Build(dispute, DisputeViewerRole.Seller);

        Assert.NotNull(dto.Money);
        Assert.Equal("PAYOUT", dto.Money!.Kind);
        Assert.Equal(1_400_000m, dto.Money.Amount);
        Assert.Equal("PROCESSING", dto.Money.Status);
    }

    [Fact]
    public void Build_WhenSellerHasNoPayoutRowYet_FallsBackToPendingNetAmount()
    {
        var dispute = NewDispute(harvestedAt: Harvested, resolution: DisputeResolution.ReleaseSeller);

        var dto = MyDisputeProgressMapper.Build(dispute, DisputeViewerRole.Seller);

        Assert.NotNull(dto.Money);
        Assert.Equal(1_450_000m, dto.Money!.Amount);
        Assert.Equal("PENDING", dto.Money.Status);
        Assert.Null(dto.Money.ProcessedAt);
    }

    [Theory]
    [InlineData(DisputeResolution.ReleaseSeller, DisputeViewerRole.Buyer)]
    [InlineData(DisputeResolution.RefundBuyer, DisputeViewerRole.Seller)]
    public void Build_WhenViewerReceivesNothing_ReturnsNoMoney(DisputeResolution resolution, DisputeViewerRole viewer)
    {
        var dispute = NewDispute(harvestedAt: Harvested, resolution: resolution);

        var dto = MyDisputeProgressMapper.Build(dispute, viewer);

        Assert.Null(dto.Money);
    }

    [Fact]
    public void Build_ShowsDescriptionOnlyToBuyer()
    {
        var dispute = NewDispute();

        var buyerView = MyDisputeProgressMapper.Build(dispute, DisputeViewerRole.Buyer);
        var sellerView = MyDisputeProgressMapper.Build(dispute, DisputeViewerRole.Seller);

        Assert.Equal("Vé không vào được cổng.", buyerView.Description);
        Assert.Equal("BUYER", buyerView.ViewerRole);
        Assert.Null(sellerView.Description);
        Assert.Equal("SELLER", sellerView.ViewerRole);
        Assert.Equal(DisputeReasonCode.TicketInvalid, sellerView.ReasonCode);
        Assert.Equal("DP-TEST", sellerView.DisputeCode);
    }

    private static Dispute NewDispute(
        DateTimeOffset? harvestedAt = null,
        DisputeResolution? resolution = null,
        DisputeStatus? status = null)
    {
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            TotalBuyerPaid = 1_550_000m,
            NetSellerPayout = 1_450_000m,
            Status = EscrowStatus.Disputed
        };
        var decidedStatus = resolution switch
        {
            DisputeResolution.RefundBuyer => DisputeStatus.Resolved,
            DisputeResolution.ReleaseSeller => DisputeStatus.Rejected,
            _ => DisputeStatus.Open
        };
        return new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow.Id,
            BuyerId = escrow.BuyerId,
            DisputeCode = "DP-TEST",
            ReasonCode = DisputeReasonCode.TicketInvalid,
            Description = "Vé không vào được cổng.",
            Status = status ?? decidedStatus,
            Resolution = resolution,
            HarvestedAt = harvestedAt,
            ResolvedAt = resolution == null ? null : Resolved,
            CreatedAt = Created,
            Escrow = escrow
        };
    }

    private static void AssertTimeline(MyDisputeProgressDto dto, params (string State, DateTimeOffset? At)[] expected)
    {
        Assert.Equal(new[] { "SUBMITTED", "GATE_LOG_CHECK", "RESOLVED" }, dto.Timeline.Select(step => step.Step));
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].State, dto.Timeline[index].State);
            Assert.Equal(expected[index].At, dto.Timeline[index].At);
        }
    }
}
