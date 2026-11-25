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
        var email = scope.ServiceProvider.GetService<IEmailService>();

        var queued = await db.EscrowTransactions.AsNoTracking()
            .Where(e => e.Status == EscrowStatus.RefundQueued)
            .Select(e => new
            {
                e.Id,
                e.PaymentReference,
                e.TotalBuyerPaid,
                e.RecipientEmail,
                BuyerEmail = e.Buyer.Email,
                BuyerName = e.Buyer.FullName
            })
            .ToListAsync(ct);

        var refunded = 0;
        foreach (var escrow in queued)
        {
            // Conditional update: a row another instance already refunded is skipped, so the buyer is emailed once.
            var now = DateTimeOffset.UtcNow;
            var changed = await db.EscrowTransactions
                .Where(e => e.Id == escrow.Id && e.Status == EscrowStatus.RefundQueued)
                .ExecuteUpdateAsync(set => set.SetProperty(e => e.Status, EscrowStatus.Refunded)
                    .SetProperty(e => e.UpdatedAt, now), ct);
            if (changed == 0) continue;
            refunded++;

            var to = string.IsNullOrWhiteSpace(escrow.RecipientEmail) ? escrow.BuyerEmail : escrow.RecipientEmail;
            if (email is null || string.IsNullOrWhiteSpace(to)) continue;
            try
            {
                await email.SendEmailAsync(
                    to,
                    "TicketShield — Your refund is complete",
                    $"<p>Hello {System.Net.WebUtility.HtmlEncode(escrow.BuyerName)},</p>" +
                    $"<p>The refund of {escrow.TotalBuyerPaid:N0} VNĐ for transaction {System.Net.WebUtility.HtmlEncode(escrow.PaymentReference)} has been completed.</p>",
                    ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not send the refund email for EscrowId {EscrowId}.", escrow.Id);
            }
        }

        return refunded;
    }
}
