using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Workers;

/// <summary>
/// Recovers escrows left in Releasing after the payout call stopped before a receipt was stored.
/// </summary>
public class StuckSettlementWorker : BackgroundService
{
    public static readonly TimeSpan DefaultStuckAge = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StuckSettlementWorker> _logger;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _stuckAge;

    public StuckSettlementWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<StuckSettlementWorker> logger,
        TimeSpan? checkInterval = null,
        TimeSpan? stuckAge = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(60);
        _stuckAge = stuckAge ?? DefaultStuckAge;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "StuckSettlementWorker started. Interval {Interval} seconds, stuck age {Age} minutes.",
            _checkInterval.TotalSeconds,
            _stuckAge.TotalMinutes);

        using var timer = new PeriodicTimer(_checkInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReconcileStuckReleasesAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "An error occurred while recovering escrows stuck in Releasing.");
            }
        }
    }

    public async Task<int> ReconcileStuckReleasesAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ITicketShieldDbContext>();
        var settler = scope.ServiceProvider.GetRequiredService<IEscrowPayoutSettler>();

        var cutoff = DateTimeOffset.UtcNow - _stuckAge;
        var stuckEscrowIds = await dbContext.EscrowTransactions
            .Where(escrow => escrow.Status == EscrowStatus.Releasing && escrow.UpdatedAt <= cutoff)
            .Where(escrow => escrow.PayoutTransaction == null
                || (escrow.PayoutTransaction.Status != PayoutStatus.Failed
                    && escrow.PayoutTransaction.RetryCount < 3))
            .Select(escrow => escrow.Id)
            .ToListAsync(ct);

        var recovered = 0;
        foreach (var escrowId in stuckEscrowIds)
        {
            if (await settler.TryResumeReleaseAsync(escrowId, ct))
            {
                recovered++;
            }
        }

        return recovered;
    }
}
