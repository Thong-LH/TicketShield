using System.Linq.Expressions;
using TicketShield.Domain.Common;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Domain.Entities;

public class ResaleListing : BaseEntity
{
    /// <summary>
    /// Domain Law 2 (Bundle Size Law): một gói vé chỉ được chứa từ 2 đến <see cref="MaxBundleTickets"/> vé.
    /// Mỗi vé trong gói phải có phiên OTP riêng đã xác thực — không được suy diễn vé từ vé khác.
    /// FIX Lỗi 12: Nâng lên 4 theo đặc tả mới (trước là 3).
    /// </summary>
    public const int MaxBundleTickets = 4;

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
    public string? SeatZone { get; set; }

    // Bundle support (BE-CORE-5.2.2)
    public Guid? BundleId { get; set; }
    public bool IsBundleAllOrNothing { get; set; } = false;
    public int BundleTotalTickets { get; set; } = 0;

    // Ticket reference (US-12.3 Decoupling Hardening: Bắt buộc 1 Listing phải gắn với 1 Physical Ticket)
    public Guid TicketId { get; set; }

    // Navigation
    public Ticket Ticket { get; set; } = null!;
    public Event Event { get; set; } = null!;
    public TicketTier Tier { get; set; } = null!;
    public ShadowUser Seller { get; set; } = null!;
    public ICollection<EscrowTransaction> EscrowTransactions { get; set; } = new List<EscrowTransaction>();
    public ICollection<EscrowTransactionListing> EscrowListings { get; set; } = new List<EscrowTransactionListing>();
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
                $"Resale price ({ResalePrice:N0} VNĐ) cannot exceed the price ceiling of {ceiling:N0} VNĐ (original price {OriginalPrice:N0} VNĐ + {percent:0.##}%).");
        }
    }

    /// <summary>
    /// Admin price-ceiling inspection ("At cap only" filter and the "At cap" card): the listing is at or over
    /// its ceiling. A 0% ceiling sold exactly at face value does not count, because that is not a markup;
    /// selling above face value on a 0% ceiling is still over the cap and does count.
    /// Uses the same formula as <see cref="ComputePriceCeiling"/> so it translates to SQL.
    /// </summary>
    public static readonly Expression<Func<ResaleListing, bool>> IsAtOrOverCeiling = l =>
        l.ResalePrice > Math.Truncate(l.OriginalPrice * (1m + l.AppliedMarkupPercentage / 100m)) ||
        (l.AppliedMarkupPercentage > 0m &&
         l.ResalePrice >= Math.Truncate(l.OriginalPrice * (1m + l.AppliedMarkupPercentage / 100m)));

    public static decimal ComputePriceCeiling(decimal originalPrice, decimal markupPercent)
    {
        if (markupPercent < 0m || markupPercent > 100m)
        {
            throw new BusinessRuleViolationException("Price ceiling markup percentage must be between 0% and 100%.");
        }

        return decimal.Truncate(originalPrice * (1m + markupPercent / 100m));
    }
}
