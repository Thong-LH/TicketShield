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
        var expiredListings = await dbContext.ResaleListings
            .Include(l => l.EscrowTransactions)
            .Where(l => l.ListingStatus == ListingStatus.Transacting &&
                        l.EscrowTransactions.Any(e => e.Status == EscrowStatus.Pending &&
                                                     e.UnlockAt.HasValue &&
                                                     e.UnlockAt.Value <= now))
            .ToListAsync(ct);

        if (!expiredListings.Any())
        {
            return 0;
        }

        foreach (var listing in expiredListings)
        {
            listing.ListingStatus = ListingStatus.Verified;
            var expiredEscrow = listing.EscrowTransactions
                .Where(e => e.Status == EscrowStatus.Pending && e.UnlockAt.HasValue && e.UnlockAt.Value <= now)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefault();

            _logger.LogInformation(
                "Released expired hold for ListingId: {ListingId} (Hold expired at {UnlockAt}). Status reset to VERIFIED.",
                listing.Id, expiredEscrow?.UnlockAt);
        }

        await dbContext.SaveChangesAsync(ct);
        return expiredListings.Count;
    }
}
