using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Workers;

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
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(3);
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

        // FIX Lỗi 03: Non-blocking Distributed Advisory Lock (Leader Election)
        var lockKey = ComputeWorkerLockKey(nameof(AutomaticSettlementWorker));
        await using var lockTx = await dbContext.TryBeginAdvisoryLockTransactionAsync(lockKey, ct);
        if (lockTx == null)
        {
            _logger.LogDebug("Another instance is already processing automatic settlements. Skipping cycle.");
            return 0;
        }

        var settler = scope.ServiceProvider.GetRequiredService<IEscrowPayoutSettler>();
        var now = DateTimeOffset.UtcNow;
        var dueEscrowIds = await dbContext.EscrowTransactions
            .Include(escrow => escrow.Seller)
            .Where(escrow => escrow.Status == EscrowStatus.Locked &&
                             escrow.InSettlementBuffer &&
                             escrow.UnlockAt.HasValue &&
                             escrow.UnlockAt.Value <= now &&
                             // FIX Lỗi 01: Bỏ qua escrow của seller chưa liên kết STK.
                             // Tránh vòng lặp quét DB vô tận 60s/lần khi TrySettleAsync luôn return false.
                             // Escrow sẽ tự được quét lại ngay sau khi Seller liên kết STK qua LinkBankAccountCommand.
                             !string.IsNullOrEmpty(escrow.Seller.PayoutAccountNumber))
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

    private static long ComputeWorkerLockKey(string workerName)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("ts:worker:" + workerName));
        return BitConverter.ToInt64(hash, 0);
    }
}
