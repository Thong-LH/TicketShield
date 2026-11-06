using TicketShield.Domain.Common;

namespace TicketShield.Domain.Entities;

public class Commission : BaseEntity
{
    public Guid? EventId { get; set; }
    public decimal BuyerFeeRate { get; set; }
    public decimal SellerFeeRate { get; set; }
    public decimal MinimumBuyerFee { get; set; }
    public decimal MinimumSellerFee { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Event? Event { get; set; }
}
