using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Workers;

/// <summary>
/// Background worker to automatically disburse funds (Payout) to sellers once the Escrow settlement buffer expires (UnlockAt reached).
/// </summary>
public class AutomaticSettlementWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutomaticSettlementWorker> _logger;
    private readonly TimeSpan _checkInterval;

    public AutomaticSettlementWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<AutomaticSettlementWorker> logger,
        TimeSpan? checkInterval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutomaticSettlementWorker started with check interval {Interval} seconds.", _checkInterval.TotalSeconds);

        using var timer = new PeriodicTimer(_checkInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessAutomaticPayoutsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "An error occurred while executing automatic seller payout settlements.");
            }
        }
    }

    public async Task<int> ProcessAutomaticPayoutsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ITicketShieldDbContext>();
        var settler = scope.ServiceProvider.GetRequiredService<IEscrowPayoutSettler>();

        var now = DateTimeOffset.UtcNow;
        var dueEscrowIds = await dbContext.EscrowTransactions
            .Where(escrow => escrow.Status == EscrowStatus.Locked &&
                             escrow.InSettlementBuffer &&
                             escrow.UnlockAt.HasValue &&
                             escrow.UnlockAt.Value <= now)
            .Select(escrow => escrow.Id)
            .ToListAsync(ct);

        var settled = 0;
        foreach (var escrowId in dueEscrowIds)
        {
            if (await settler.TrySettleAsync(escrowId, ct))
            {
                settled++;
            }
        }

        return settled;
    }
}
