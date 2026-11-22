using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Workers;
using Xunit;

namespace TicketShield.Resale.Tests;

public class SimulatedRefundTests(ResaleFixture fixture) : IClassFixture<ResaleFixture>
{
    [Fact]
    public async Task PaymentForExpiredHoldCompletesSimulatedRefundAndReplayIsIdempotent()
    {
        using var scope = fixture.Core.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketShieldDbContext>();
        var escrow = await db.EscrowTransactions.Include(e => e.Listing).FirstAsync();
        escrow.Status = EscrowStatus.Expired;
        escrow.UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        escrow.BankTransactionReference = null;
        escrow.PaymentReference = "TS1A2B3C4D";
        escrow.Listing.ListingStatus = ListingStatus.Verified;
        await db.SaveChangesAsync();

        var handler = new ProcessSePayWebhookCommandHandler(db);
        var command = new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 4321, TransferType = "in", TransferAmount = escrow.TotalBuyerPaid,
            Content = escrow.PaymentReference, ReferenceCode = "TEST-LATE-PAYMENT"
        });
        var received = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("RefundQueued", received.Data!.EscrowStatus);
        Assert.Equal(ListingStatus.Verified, escrow.Listing.ListingStatus);

        var worker = ActivatorUtilities.CreateInstance<SimulatedRefundWorker>(scope.ServiceProvider);
        Assert.Equal(1, await worker.ProcessQueuedAsync(CancellationToken.None));
        Assert.Equal(0, await worker.ProcessQueuedAsync(CancellationToken.None));
        db.ChangeTracker.Clear();
        var replay = await handler.Handle(command, CancellationToken.None);
        Assert.True(replay.Data!.IsIdempotentDuplicate);
        Assert.Equal("Refunded", replay.Data.EscrowStatus);
    }
}
