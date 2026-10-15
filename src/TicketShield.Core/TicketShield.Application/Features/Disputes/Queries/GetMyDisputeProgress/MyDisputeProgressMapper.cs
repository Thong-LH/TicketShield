using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Disputes.Queries.GetMyDisputeProgress;

public enum DisputeViewerRole
{
    Buyer,
    Seller
}

public static class MyDisputeProgressMapper
{
    public const string StepSubmitted = "SUBMITTED";
    public const string StepGateLogCheck = "GATE_LOG_CHECK";
    public const string StepResolved = "RESOLVED";

    public const string StateDone = "DONE";
    public const string StateCurrent = "CURRENT";
    public const string StatePending = "PENDING";
    public const string StateSkipped = "SKIPPED";

    public const string BuyerRefundedMessage = "Khiếu nại của bạn được chấp nhận. Bạn được hoàn lại số tiền đã thanh toán.";
    public const string BuyerRejectedMessage = "Khiếu nại chưa đủ căn cứ để hoàn tiền. Khoản thanh toán được chuyển cho người bán.";
    public const string SellerRefundedMessage = "Khiếu nại của người mua được chấp nhận. Khoản thanh toán được hoàn lại cho người mua.";
    public const string SellerRejectedMessage = "Khiếu nại của người mua không được chấp nhận. Tiền bán vé sẽ được chuyển cho bạn.";

    // Cần dispute đã nạp Escrow và Escrow.PayoutTransaction.
    public static MyDisputeProgressDto Build(Dispute dispute, DisputeViewerRole viewer)
    {
        var decided = dispute.Status is DisputeStatus.Resolved or DisputeStatus.Rejected;
        var resolution = decided ? dispute.Resolution : null;

        return new MyDisputeProgressDto
        {
            DisputeId = dispute.Id,
            DisputeCode = dispute.DisputeCode,
            ViewerRole = viewer == DisputeViewerRole.Buyer ? "BUYER" : "SELLER",
            ReasonCode = dispute.ReasonCode,
            Status = decided ? dispute.Status.ToString().ToUpperInvariant() : "OPEN",
            CreatedAt = dispute.CreatedAt,
            Description = viewer == DisputeViewerRole.Buyer ? dispute.Description : null,
            Timeline = BuildTimeline(dispute, decided),
            Outcome = resolution switch
            {
                DisputeResolution.RefundBuyer => "REFUND_BUYER",
                DisputeResolution.ReleaseSeller => "RELEASE_SELLER",
                _ => null
            },
            ResolutionMessage = BuildMessage(resolution, viewer),
            Money = BuildMoney(dispute, resolution, viewer)
        };
    }

    private static List<DisputeTimelineStepDto> BuildTimeline(Dispute dispute, bool decided)
    {
        var harvested = dispute.HarvestedAt.HasValue;

        var gateLog = new DisputeTimelineStepDto { Step = StepGateLogCheck };
        if (harvested)
        {
            gateLog.State = StateDone;
            gateLog.At = dispute.HarvestedAt;
        }
        else
        {
            gateLog.State = decided ? StateSkipped : StateCurrent;
        }

        var resolved = new DisputeTimelineStepDto { Step = StepResolved };
        if (decided)
        {
            resolved.State = StateDone;
            resolved.At = dispute.ResolvedAt;
        }
        else
        {
            resolved.State = harvested ? StateCurrent : StatePending;
        }

        return new List<DisputeTimelineStepDto>
        {
            new() { Step = StepSubmitted, State = StateDone, At = dispute.CreatedAt },
            gateLog,
            resolved
        };
    }

    private static string? BuildMessage(DisputeResolution? resolution, DisputeViewerRole viewer) =>
        (resolution, viewer) switch
        {
            (DisputeResolution.RefundBuyer, DisputeViewerRole.Buyer) => BuyerRefundedMessage,
            (DisputeResolution.ReleaseSeller, DisputeViewerRole.Buyer) => BuyerRejectedMessage,
            (DisputeResolution.RefundBuyer, DisputeViewerRole.Seller) => SellerRefundedMessage,
            (DisputeResolution.ReleaseSeller, DisputeViewerRole.Seller) => SellerRejectedMessage,
            _ => null
        };

    private static DisputeMoneyDto? BuildMoney(Dispute dispute, DisputeResolution? resolution, DisputeViewerRole viewer)
    {
        if (resolution == DisputeResolution.RefundBuyer && viewer == DisputeViewerRole.Buyer)
        {
            // Hệ thống mới chốt quyết định hoàn, chưa có bước chuyển tiền thật.
            return new DisputeMoneyDto
            {
                Kind = "REFUND",
                Amount = dispute.RefundAmount,
                Status = "APPROVED"
            };
        }

        if (resolution == DisputeResolution.ReleaseSeller && viewer == DisputeViewerRole.Seller)
        {
            var payout = dispute.Escrow?.PayoutTransaction;
            return new DisputeMoneyDto
            {
                Kind = "PAYOUT",
                Amount = payout?.Amount ?? dispute.Escrow?.NetSellerPayout ?? 0,
                Status = (payout?.Status ?? PayoutStatus.Pending).ToString().ToUpperInvariant(),
                ProcessedAt = payout?.ProcessedAt
            };
        }

        return null;
    }
}
