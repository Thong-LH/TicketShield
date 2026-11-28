using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;
using TicketShield.Domain.Entities;

namespace TicketShield.Application.Features.ResaleListings.Common;

public static class ResaleListingExtensions
{
    public static ResaleListingDetailDto ToDetailDto(this ResaleListing listing, DateTimeOffset? bundleUnlockAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        // A listing can retain historical escrow rows. Never use the newest
        // row blindly: the marketplace countdown must reflect the active hold
        // with the actual latest expiry returned by checkout.
        var activeDirectHold = listing.EscrowTransactions
            .Where(e => e.Status == TicketShield.Domain.Enums.EscrowStatus.Pending && e.UnlockAt.HasValue && e.UnlockAt.Value > now)
            .OrderByDescending(e => e.UnlockAt)
            .ThenByDescending(e => e.CreatedAt)
            .FirstOrDefault();
        var unlockAt = bundleUnlockAt ?? activeDirectHold?.UnlockAt ?? listing.EscrowTransaction?.UnlockAt;
        var listingStatus = listing.ListingStatus.ToString();

        // If marked Transacting, check if hold expired in realtime
        if (listing.ListingStatus == TicketShield.Domain.Enums.ListingStatus.Transacting)
        {
            var hasBlockingEscrow = listing.EscrowTransactions
                .Any(e => e.Status == TicketShield.Domain.Enums.EscrowStatus.TransferNeedsReview ||
                          e.Status == TicketShield.Domain.Enums.EscrowStatus.Locked);
            var hasActiveHold = bundleUnlockAt.HasValue && bundleUnlockAt.Value > now || activeDirectHold != null;

            // A previous partial bundle release can leave a sibling marked
            // Transacting with no pending escrow at all. It is not locked and
            // must never make clients reuse an old local countdown.
            if (!hasBlockingEscrow && !hasActiveHold)
            {
                var pastCutoff = listing.Event != null && listing.Event.EventStartAt.AddHours(-2) <= now;
                listingStatus = pastCutoff
                    ? TicketShield.Domain.Enums.ListingStatus.Expired.ToString()
                    : TicketShield.Domain.Enums.ListingStatus.Verified.ToString();
                unlockAt = null;
            }
            else if (hasBlockingEscrow)
            {
                unlockAt = null; // Ẩn thời gian đếm ngược giữ chỗ vì vé đang đối soát/thanh toán
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
            EventBannerUrl = listing.Event?.BannerUrl,
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
            ServerNow = now,
            CreatedAt = listing.CreatedAt,
            BundleId = listing.BundleId,
            IsBundleAllOrNothing = listing.IsBundleAllOrNothing,
            BundleTotalTickets = listing.BundleTotalTickets,
            PrivateAccessToken = listing.PrivateAccessToken
        };
    }
}
