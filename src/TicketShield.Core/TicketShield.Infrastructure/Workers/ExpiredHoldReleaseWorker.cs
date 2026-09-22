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

        using var timer = new PeriodicTimer(_checkInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
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

    public async Task<int> ReleaseExpiredHoldsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ITicketShieldDbContext>();

        var now = DateTimeOffset.UtcNow;
        var listings = await dbContext.ResaleListings
            .Include(l => l.EscrowTransactions)
            .Include(l => l.Event)
            .Where(l => l.EscrowTransactions.Any(e => e.Status == EscrowStatus.Pending &&
                                                     e.UnlockAt.HasValue &&
                                                     e.UnlockAt.Value <= now))
            .ToListAsync(ct);

        if (!listings.Any())
        {
            return 0;
        }

        var revertedCount = 0;
        foreach (var listing in listings)
        {
            var expiredEscrows = listing.EscrowTransactions
                .Where(e => e.Status == EscrowStatus.Pending && e.UnlockAt.HasValue && e.UnlockAt.Value <= now)
                .ToList();
            foreach (var expired in expiredEscrows)
            {
                expired.Status = EscrowStatus.Expired;
            }

            var hasActiveHold = listing.EscrowTransactions.Any(e =>
                e.Status == EscrowStatus.Pending && e.UnlockAt.HasValue && e.UnlockAt.Value > now);

            if (listing.ListingStatus == ListingStatus.Transacting && !hasActiveHold)
            {
                var pastResaleCutoff = listing.Event != null &&
                    listing.Event.EventStartAt.AddHours(-2) <= now;
                listing.ListingStatus = pastResaleCutoff
                    ? ListingStatus.Expired
                    : ListingStatus.Verified;
                revertedCount++;
                _logger.LogInformation(
                    "Released expired hold for ListingId: {ListingId} (Hold expired at {UnlockAt}). Status set to {ListingStatus}.",
                    listing.Id, expiredEscrows.Max(e => e.UnlockAt), listing.ListingStatus);
            }
        }

        await dbContext.SaveChangesAsync(ct);
        return revertedCount;
    }
}
