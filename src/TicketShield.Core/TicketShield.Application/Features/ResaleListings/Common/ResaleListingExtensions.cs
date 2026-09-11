using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;
using TicketShield.Domain.Entities;

namespace TicketShield.Application.Features.ResaleListings.Common;

public static class ResaleListingExtensions
{
    public static ResaleListingDetailDto ToDetailDto(this ResaleListing listing)
    {
        return new ResaleListingDetailDto
        {
            ListingId = listing.Id,
            EventId = listing.EventId,
            EventName = listing.Event?.Name ?? string.Empty,
            EventVenue = listing.Event?.Venue ?? string.Empty,
            EventStartAt = listing.Event?.EventStartAt ?? default,
            TierId = listing.TierId,
            TierName = listing.Tier?.TierName ?? string.Empty,
            OriginalPrice = listing.OriginalPrice,
            ResalePrice = listing.ResalePrice,
            DiscountAmount = listing.DiscountAmount,
            DiscountPercentage = listing.DiscountPercentage,
            IsPrivate = listing.IsPrivate,
            MaskedTicketCode = listing.MaskedTicketCode,
            VerificationStatus = listing.VerificationStatus.ToString(),
            ListingStatus = listing.ListingStatus.ToString(),
            SellerId = listing.SellerId,
            SellerFullName = listing.Seller?.FullName ?? string.Empty,
            CreatedAt = listing.CreatedAt
        };
    }
}
