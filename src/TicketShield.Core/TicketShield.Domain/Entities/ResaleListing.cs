using TicketShield.Domain.Common;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Domain.Entities;

public class ResaleListing : BaseEntity
{
    public Guid EventId { get; set; }
    public Guid TierId { get; set; }
    public Guid SellerId { get; set; }
    public string OriginalTicketCode { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public decimal ResalePrice { get; set; }

    /// <summary>
    /// Markup percent copied from the Event at publish time. Later Event edits do not change live listings.
    /// </summary>
    public decimal AppliedMarkupPercentage { get; set; }

    public bool IsPrivate { get; set; } = false;
    public string? PrivateAccessToken { get; set; }
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Verified;
    public ListingStatus ListingStatus { get; set; } = ListingStatus.Verified;

    // Navigation
    public Event Event { get; set; } = null!;
    public TicketTier Tier { get; set; } = null!;
    public ShadowUser Seller { get; set; } = null!;
    public ICollection<EscrowTransaction> EscrowTransactions { get; set; } = new List<EscrowTransaction>();
    public EscrowTransaction? EscrowTransaction
    {
        get => EscrowTransactions.OrderByDescending(e => e.CreatedAt).FirstOrDefault();
        set
        {
            if (value != null && !EscrowTransactions.Contains(value))
            {
                EscrowTransactions.Add(value);
            }
        }
    }

    // Computed Domain Properties
    public decimal DiscountAmount => OriginalPrice > ResalePrice ? OriginalPrice - ResalePrice : 0;
    public decimal DiscountPercentage => OriginalPrice > 0 ? Math.Round((DiscountAmount / OriginalPrice) * 100, 1) : 0;
    public string MaskedTicketCode => OriginalTicketCode.Length <= 4
        ? "****"
        : string.Concat(OriginalTicketCode.AsSpan(0, 2), new string('*', OriginalTicketCode.Length - 4), OriginalTicketCode.AsSpan(OriginalTicketCode.Length - 2));

    /// <summary>
    /// Global Law 1 (Price Ceiling Law): ResalePrice must be at or below
    /// Truncate(OriginalPrice × (1 + markupPercent / 100)).
    /// </summary>
    public void ValidatePriceCeiling(decimal? markupPercent = null)
    {
        var percent = markupPercent ?? AppliedMarkupPercentage;
        var ceiling = ComputePriceCeiling(OriginalPrice, percent);
        if (ResalePrice > ceiling)
        {
            throw new BusinessRuleViolationException(
                $"Giá bán lại ({ResalePrice:N0} VNĐ) không được vượt quá trần {ceiling:N0} VNĐ (giá gốc {OriginalPrice:N0} VNĐ + {percent:0.##}%).");
        }
    }

    public static decimal ComputePriceCeiling(decimal originalPrice, decimal markupPercent)
    {
        if (markupPercent < 0m || markupPercent > 100m)
        {
            throw new BusinessRuleViolationException("Biên độ trần giá (markup) phải nằm trong khoảng 0% đến 100%.");
        }

        return decimal.Truncate(originalPrice * (1m + markupPercent / 100m));
    }
}
