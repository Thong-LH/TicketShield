using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Workers;

public class ExpiredHoldReleaseWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredHoldReleaseWorker> _logger;
    private readonly TimeSpan _checkInterval;

    public ExpiredHoldReleaseWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredHoldReleaseWorker> logger,
        TimeSpan? checkInterval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(15);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExpiredHoldReleaseWorker started with check interval {Interval} seconds.", _checkInterval.TotalSeconds);

        try
        {
            using var timer = new PeriodicTimer(_checkInterval);
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ReleaseExpiredHoldsAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "An error occurred while releasing expired listing holds.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during graceful shutdown
        }
    }

    public async Task<int> ReleaseExpiredHoldsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ITicketShieldDbContext>();

        // If a webhook credits a scanned hold, retry the scan instead of overwriting RefundQueued.
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
            : null;
        var now = DateTimeOffset.UtcNow;

        // Find all expired pending escrows (both single-listing and bundle)
        var expiredEscrows = await dbContext.EscrowTransactions
            .Include(e => e.Listing)
                .ThenInclude(l => l.Event)
            .Where(e => e.Status == EscrowStatus.Pending && e.UnlockAt.HasValue && e.UnlockAt.Value <= now)
            .ToListAsync(ct);

        if (!expiredEscrows.Any())
        {
            return 0;
        }

        var revertedCount = 0;
        var processedBundleIds = new HashSet<Guid>();

        foreach (var expired in expiredEscrows)
        {
            expired.Status = EscrowStatus.Expired;

            // BE-CORE-5.2.3: Bundle-aware expiry
            if (expired.BundleId.HasValue)
            {
                if (processedBundleIds.Contains(expired.BundleId.Value))
                {
                    continue; // Already processed this bundle
                }
                processedBundleIds.Add(expired.BundleId.Value);

                // Release all listings in the bundle
                var bundleListings = await dbContext.ResaleListings
                    .Include(l => l.Event)
                    .Where(l => l.BundleId == expired.BundleId.Value)
                    .ToListAsync(ct);

                // BE-12.1.5: Guard against resetting listings that have blocking escrows (TransferNeedsReview, Locked)
                var hasBlockingBundleEscrow = await dbContext.EscrowTransactions
                    .AnyAsync(e => (e.BundleId == expired.BundleId.Value || bundleListings.Select(b => b.Id).Contains(e.ListingId)) &&
                                   (e.Status == EscrowStatus.TransferNeedsReview || e.Status == EscrowStatus.Locked), ct);

                if (!hasBlockingBundleEscrow)
                {
                    foreach (var listing in bundleListings)
                    {
                        if (listing.ListingStatus == ListingStatus.Transacting)
                        {
                            var pastResaleCutoff = listing.Event != null &&
                                listing.Event.EventStartAt.AddHours(-2) <= now;
                            listing.ListingStatus = pastResaleCutoff
                                ? ListingStatus.Expired
                                : ListingStatus.Verified;
                            revertedCount++;
                            _logger.LogInformation(
                                "Released expired bundle hold for ListingId: {ListingId} (BundleId: {BundleId}). Status set to {ListingStatus}.",
                                listing.Id, expired.BundleId.Value, listing.ListingStatus);
                        }
                    }
                }
                else
                {
                    _logger.LogInformation(
                        "Skipped releasing bundle hold for BundleId: {BundleId} due to active blocking escrow (TransferNeedsReview/Locked/Completed).",
                        expired.BundleId.Value);
                }
            }
            else
            {
                // Original single-listing logic
                var listing = expired.Listing;
                if (listing == null) continue;

                // BE-12.1.5: Guard against resetting listing if a blocking escrow exists
                var hasBlockingEscrow = await dbContext.EscrowTransactions
                    .AnyAsync(e => e.ListingId == listing.Id &&
                                   (e.Status == EscrowStatus.TransferNeedsReview || e.Status == EscrowStatus.Locked), ct);

                var hasActiveHold = await dbContext.EscrowTransactions
                    .AnyAsync(e => e.ListingId == listing.Id && e.Status == EscrowStatus.Pending
                                   && e.UnlockAt.HasValue && e.UnlockAt.Value > now && e.Id != expired.Id, ct);

                if (listing.ListingStatus == ListingStatus.Transacting && !hasActiveHold && !hasBlockingEscrow)
                {
                    var pastResaleCutoff = listing.Event != null &&
                        listing.Event.EventStartAt.AddHours(-2) <= now;
                    listing.ListingStatus = pastResaleCutoff
                        ? ListingStatus.Expired
                        : ListingStatus.Verified;
                    revertedCount++;
                    _logger.LogInformation(
                        "Released expired hold for ListingId: {ListingId} (Hold expired at {UnlockAt}). Status set to {ListingStatus}.",
                        listing.Id, expired.UnlockAt, listing.ListingStatus);
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);
        return revertedCount;
    }
}
