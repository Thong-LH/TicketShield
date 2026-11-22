using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Workers;

public sealed class SimulatedRefundWorker(IServiceScopeFactory scopes, ILogger<SimulatedRefundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var count = await ProcessQueuedAsync(stoppingToken);
                    if (count > 0) logger.LogInformation("Simulated refunds for {Count} queued transactions.", count);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Could not process simulated refunds.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during graceful shutdown
        }
    }

    public async Task<int> ProcessQueuedAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ITicketShieldDbContext>();
        var now = DateTimeOffset.UtcNow;
        return await db.EscrowTransactions.Where(e => e.Status == EscrowStatus.RefundQueued)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.Status, EscrowStatus.Refunded)
                .SetProperty(e => e.UpdatedAt, now), ct);
    }
}
