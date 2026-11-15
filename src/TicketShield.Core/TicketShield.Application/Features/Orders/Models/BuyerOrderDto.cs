using System.Text.Json.Serialization;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Orders.Models;

/// <summary>
/// DTO chi tiết đơn hàng và sổ cái tài chính của người mua (Buyer)
/// Đáp ứng yêu cầu SCRUM-340 / SCRUM-341 (BE-11.2.1)
/// </summary>
public class BuyerOrderDto
{
    public Guid OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public Guid ListingId { get; set; }

    // Bundle info
    public Guid? BundleId { get; set; }
    public bool IsBundle => BundleId.HasValue;
    public int BundleTotalTickets { get; set; }

    // Event & Ticket info
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string EventVenue { get; set; } = string.Empty;
    public DateTimeOffset EventStartAt { get; set; }
    public string TierName { get; set; } = string.Empty;
    public string? SeatZone { get; set; }
    public string? TicketCode { get; set; }
    /// <summary>Từng vé trong đơn kèm khu ghế (Combo có nhiều vé). TicketCode ở trên giữ nguyên cho FE cũ.</summary>
    public List<BuyerOrderTicketDto> Tickets { get; set; } = new();

    // Financial Ledger (Số tiền đã chuyển khoản qua VietQR & Mã tham chiếu SePay)
    public decimal OriginalTicketPrice { get; set; }
    public decimal BuyerFee { get; set; }
    public decimal TotalBuyerPaid { get; set; }
    public string? PaymentReference { get; set; }
    public string? BankTransactionReference { get; set; }

    // Status & Milestones (enum trả dạng chữ, giống các API cũ)
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EscrowStatus Status { get; set; }
    public string StatusDescription { get; set; } = string.Empty;
    public DateTimeOffset? TransferredAt { get; set; }
    public DateTimeOffset? DisputeDeadline { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Refund details (Trạng thái hoàn tiền)
    public bool IsRefunded { get; set; }
    public bool IsRefundQueued { get; set; }
    public decimal RefundAmount { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundReason { get; set; }

    // Dispute reference
    public Guid? DisputeId { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DisputeStatus? DisputeStatus { get; set; }
    public bool DisputeWaived { get; set; }
}

