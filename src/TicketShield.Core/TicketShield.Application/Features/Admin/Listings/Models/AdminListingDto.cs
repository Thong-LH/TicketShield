using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Admin.Listings.Models;

/// <summary>
/// DTO chi tiết tin bán vé lẻ hoặc gói Combo phục vụ Admin kiểm tra, giám sát trần giá
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-336 (BE-11.1.3)
/// </summary>
public class AdminListingDto
{
    public Guid ListingId { get; set; }

    // Bundle information
    public Guid? BundleId { get; set; }
    public bool IsBundle => BundleId.HasValue;
    public bool IsBundleAllOrNothing { get; set; }
    public int BundleTotalTickets { get; set; }

    // Event & Ticket information
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public Guid TierId { get; set; }
    public string TierName { get; set; } = string.Empty;
    public string OriginalTicketCode { get; set; } = string.Empty;
    public string MaskedTicketCode { get; set; } = string.Empty;
    public string? SeatZone { get; set; }
    public Guid? TicketId { get; set; }

    // Seller information
    public Guid SellerId { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public string SellerEmail { get; set; } = string.Empty;
    public string? SellerPhone { get; set; }

    // Pricing & Price Ceiling Control (Kiểm soát trần giá)
    public decimal OriginalPrice { get; set; }
    public decimal ResalePrice { get; set; }
    public decimal AppliedMarkupPercentage { get; set; }
    public decimal EventMaxMarkupPercentage { get; set; }
    public decimal MaxAllowedPrice { get; set; }
    public decimal ActualMarkupPercentage { get; set; }
    public bool IsAtPriceCeiling { get; set; }
    public bool IsPriceCeilingExceeded { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercentage { get; set; }

    // Status & Milestones
    public ListingStatus ListingStatus { get; set; }
    public VerificationStatus VerificationStatus { get; set; }
    public bool IsPrivate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

