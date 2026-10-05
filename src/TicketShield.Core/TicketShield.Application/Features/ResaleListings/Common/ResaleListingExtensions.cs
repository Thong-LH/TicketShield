using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;
using TicketShield.Domain.Entities;

namespace TicketShield.Application.Features.ResaleListings.Common;

public static class ResaleListingExtensions
{
    public static ResaleListingDetailDto ToDetailDto(this ResaleListing listing)
    {
        var now = DateTimeOffset.UtcNow;
        var unlockAt = listing.EscrowTransaction?.UnlockAt;
        var listingStatus = listing.ListingStatus.ToString();

        // If marked Transacting, check if hold expired in realtime
        if (listing.ListingStatus == TicketShield.Domain.Enums.ListingStatus.Transacting)
        {
            var hasActiveHold = listing.EscrowTransactions
                .Any(e => e.Status == TicketShield.Domain.Enums.EscrowStatus.Pending && e.UnlockAt.HasValue && e.UnlockAt.Value > now);

            if (!hasActiveHold && unlockAt.HasValue && unlockAt.Value <= now)
            {
                var pastCutoff = listing.Event != null && listing.Event.EventStartAt.AddHours(-2) <= now;
                listingStatus = pastCutoff
                    ? TicketShield.Domain.Enums.ListingStatus.Expired.ToString()
                    : TicketShield.Domain.Enums.ListingStatus.Verified.ToString();
                unlockAt = null;
            }
        }

        return new ResaleListingDetailDto
        {
            ListingId = listing.Id,
            EventId = listing.EventId,
            OrganizerId = listing.Event?.OrganizerId,
            OrganizerName = listing.Event?.Organizer?.Name,
            EventName = listing.Event?.Name ?? string.Empty,
            EventVenue = listing.Event?.Venue ?? string.Empty,
            EventStartAt = listing.Event?.EventStartAt ?? default,
            TierId = listing.TierId,
            TierName = listing.Tier?.TierName ?? string.Empty,
            SeatZone = listing.SeatZone,
            OriginalPrice = listing.OriginalPrice,
            ResalePrice = listing.ResalePrice,
            DiscountAmount = listing.DiscountAmount,
            DiscountPercentage = listing.DiscountPercentage,
            IsPrivate = listing.IsPrivate,
            MaskedTicketCode = listing.MaskedTicketCode,
            VerificationStatus = listing.VerificationStatus.ToString(),
            ListingStatus = listingStatus,
            SellerId = listing.SellerId,
            SellerFullName = listing.Seller?.FullName ?? string.Empty,
            UnlockAt = unlockAt,
            CreatedAt = listing.CreatedAt,
            BundleId = listing.BundleId,
            IsBundleAllOrNothing = listing.IsBundleAllOrNothing,
            BundleTotalTickets = listing.BundleTotalTickets,
            PrivateAccessToken = listing.PrivateAccessToken
        };
    }
}
