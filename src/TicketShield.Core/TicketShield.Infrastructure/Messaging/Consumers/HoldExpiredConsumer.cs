using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Contracts.Events;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Messaging.Consumers;

public class HoldExpiredConsumer : IConsumer<IHoldExpiredEvent>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ILogger<HoldExpiredConsumer> _logger;

    public HoldExpiredConsumer(ITicketShieldDbContext dbContext, ILogger<HoldExpiredConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IHoldExpiredEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("Processing scheduled hold expiration for EscrowId: {EscrowId}, ListingId: {ListingId}", message.EscrowId, message.ListingId);

        var escrow = await _dbContext.EscrowTransactions
            .Include(e => e.Listing)
                .ThenInclude(l => l.Event)
            .FirstOrDefaultAsync(e => e.Id == message.EscrowId, context.CancellationToken);

        if (escrow == null)
        {
            _logger.LogWarning("HoldExpiredConsumer: Escrow {EscrowId} not found.", message.EscrowId);
            return;
        }

        // Only release if still Pending (has not been paid, verified, or cancelled)
        if (escrow.Status == EscrowStatus.Pending)
        {
            escrow.Status = EscrowStatus.Expired;
            var now = DateTimeOffset.UtcNow;
            var listing = escrow.Listing;

            var hasActiveHold = await _dbContext.EscrowTransactions.AnyAsync(e =>
                e.ListingId == listing.Id &&
                e.Id != escrow.Id &&
                e.Status == EscrowStatus.Pending &&
                e.UnlockAt.HasValue &&
                e.UnlockAt.Value > now, context.CancellationToken);

            if (listing.ListingStatus == ListingStatus.Transacting && !hasActiveHold)
            {
                // Enforce BR-L04 / Event deadline check
                if (listing.Event != null && listing.Event.EventStartAt.AddHours(-2) <= now)
                {
                    listing.ListingStatus = ListingStatus.Expired;
                    _logger.LogInformation("Listing {ListingId} marked EXPIRED due to event resale cutoff (EventStartAt - 2h).", listing.Id);
                }
                else
                {
                    listing.ListingStatus = ListingStatus.Verified;
                    _logger.LogInformation("Listing {ListingId} reset to VERIFIED after 10-minute hold expired.", listing.Id);
                }
            }

            // FIX Lỗi 19: Khôi phục toàn bộ vé con Combo bị bỏ quên về trạng thái đúng.
            // Trước fix: chỉ vé đại diện (escrow.Listing) được reset, các vé con kẹt Transacting vĩnh viễn.
            if (escrow.BundleId.HasValue)
            {
                var bundleChildListings = await _dbContext.ResaleListings
                    .Include(l => l.Event)
                    .Where(l => l.BundleId == escrow.BundleId.Value && l.Id != escrow.ListingId)
                    .ToListAsync(context.CancellationToken);

                foreach (var childListing in bundleChildListings)
                {
                    if (childListing.ListingStatus == ListingStatus.Transacting)
                    {
                        childListing.ListingStatus = (childListing.Event != null && childListing.Event.EventStartAt.AddHours(-2) <= now)
                            ? ListingStatus.Expired
                            : ListingStatus.Verified;
                        _logger.LogInformation("Bundle child Listing {ListingId} (BundleId: {BundleId}) reset to {Status} after 10-minute hold expired.",
                            childListing.Id, escrow.BundleId.Value, childListing.ListingStatus);
                    }
                }
            }

            await _dbContext.SaveChangesAsync(context.CancellationToken);
        }
    }
}
