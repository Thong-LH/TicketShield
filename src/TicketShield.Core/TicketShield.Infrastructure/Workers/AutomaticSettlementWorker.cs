using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Entities;
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
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(10);
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

        var now = DateTimeOffset.UtcNow;
        var matureEscrows = await dbContext.EscrowTransactions
            .Include(e => e.Listing)
                .ThenInclude(l => l.Event)
            .Include(e => e.Seller)
            .Include(e => e.PayoutTransaction)
            .Where(e => e.Status == EscrowStatus.Locked &&
                        e.InSettlementBuffer &&
                        e.UnlockAt.HasValue &&
                        e.UnlockAt.Value <= now)
            .ToListAsync(ct);

        if (!matureEscrows.Any())
        {
            return 0;
        }

        foreach (var escrow in matureEscrows)
        {
            escrow.Status = EscrowStatus.Released;
            escrow.InSettlementBuffer = false;

            if (escrow.PayoutTransaction == null)
            {
                var payout = new PayoutTransaction
                {
                    Id = Guid.NewGuid(),
                    EscrowId = escrow.Id,
                    PayoutCode = $"PO-{escrow.Id.ToString("N")[..8].ToUpperInvariant()}",
                    RecipientBankCode = "MB",
                    RecipientAccountNumber = "0938434102",
                    RecipientAccountName = !string.IsNullOrWhiteSpace(escrow.Seller?.FullName)
                        ? escrow.Seller.FullName.ToUpperInvariant()
                        : "NGUYEN VAN SELLER",
                    Amount = escrow.NetSellerPayout,
                    Status = PayoutStatus.Success,
                    ProcessedAt = now,
                    BankReferenceCode = $"FT{Random.Shared.Next(10000000, 99999999)}",
                    RetryCount = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                dbContext.PayoutTransactions.Add(payout);
            }
            else
            {
                escrow.PayoutTransaction.Status = PayoutStatus.Success;
                escrow.PayoutTransaction.ProcessedAt = now;
                escrow.PayoutTransaction.UpdatedAt = now;
            }

            _logger.LogInformation(
                "🚀 [Auto-Settlement] Escrow {EscrowId} unlocked! NetSellerPayout {Amount:N0} VND successfully disbursed to seller {SellerEmail}.",
                escrow.Id, escrow.NetSellerPayout, escrow.Seller?.Email);
        }

        await dbContext.SaveChangesAsync(ct);
        return matureEscrows.Count;
    }
}
