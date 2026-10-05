using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Workers;

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
        var db = scope.ServiceProvider.GetRequiredService<ITicketShieldDbContext>();

        // FIX Lỗi 03: Non-blocking Distributed Advisory Lock (Leader Election)
        var lockKey = ComputeWorkerLockKey(nameof(StuckSettlementWorker));
        await using var lockTx = await db.TryBeginAdvisoryLockTransactionAsync(lockKey, ct);
        if (lockTx == null)
        {
            _logger.LogDebug("Another instance is already reconciling stuck settlements. Skipping cycle.");
            return 0;
        }

        var client = scope.ServiceProvider.GetRequiredService<ISettlementClient>();
        var applier = scope.ServiceProvider.GetRequiredService<IPayoutReportApplier>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<PayoutOutboxDispatcher>();
        var cutoff = DateTimeOffset.UtcNow - _stuckAge;
        var stuckIds = await db.EscrowTransactions
            .Where(escrow => escrow.Status == EscrowStatus.Releasing && escrow.UpdatedAt <= cutoff)
            .Select(escrow => escrow.Id)
            .ToListAsync(ct);

        var handled = 0;
        foreach (var escrowId in stuckIds)
        {
            var message = await db.OutboxMessages
                .Where(row => row.EventType == nameof(PayoutRequestedEvent) && row.Payload.Contains(escrowId.ToString()))
                .OrderByDescending(row => row.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (message == null)
            {
                continue;
            }

            if (message.ProcessedAt == null)
            {
                var sent = await dispatcher.DispatchPendingAsync(ct);
                if (sent > 0)
                {
                    handled++;
                }

                continue;
            }

            var command = JsonSerializer.Deserialize<PayoutRequestedEvent>(message.Payload);
            if (command == null)
            {
                continue;
            }

            var status = await client.GetStatusAsync(command.IdempotencyKey, ct);
            if (status == null)
            {
                continue;
            }

            if (status.State == "Succeeded" && await applier.ApplyAsync(escrowId, succeeded: true, status.BankReference, ct))
            {
                handled++;
            }
            else if ((status.State == "Failed" || status.State == "Exhausted") && await applier.ApplyAsync(escrowId, succeeded: false, bankReference: null, ct))
            {
                handled++;
            }
        }

        return handled;
    }

    private static long ComputeWorkerLockKey(string workerName)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("ts:worker:" + workerName));
        return BitConverter.ToInt64(hash, 0);
    }
}
