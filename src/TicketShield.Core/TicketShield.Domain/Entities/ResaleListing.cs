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
    public bool IsPrivate { get; set; } = false;
    public string? PrivateAccessToken { get; set; }
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Verified;
    public ListingStatus ListingStatus { get; set; } = ListingStatus.Verified;

    // Navigation
    public Event Event { get; set; } = null!;
    public TicketTier Tier { get; set; } = null!;
    public User Seller { get; set; } = null!;
    public EscrowTransaction? EscrowTransaction { get; set; }

    // Computed Domain Properties
    public decimal DiscountAmount => OriginalPrice > ResalePrice ? OriginalPrice - ResalePrice : 0;
    public decimal DiscountPercentage => OriginalPrice > 0 ? Math.Round((DiscountAmount / OriginalPrice) * 100, 1) : 0;
    public string MaskedTicketCode => OriginalTicketCode.Length <= 4
        ? "****"
        : string.Concat(OriginalTicketCode.AsSpan(0, 2), new string('*', OriginalTicketCode.Length - 4), OriginalTicketCode.AsSpan(OriginalTicketCode.Length - 2));

    /// <summary>
    /// Global Law 1 (Price Ceiling Law): ResalePrice must be <= OriginalPrice.
    /// </summary>
    public void ValidatePriceCeiling()
    {
        if (ResalePrice > OriginalPrice)
        {
            throw new BusinessRuleViolationException(
                $"Giá bán lại ({ResalePrice:N0} VNĐ) không được vượt quá giá gốc ({OriginalPrice:N0} VNĐ) theo Quy định chống đầu cơ của TicketShield.");
        }
    }
}
