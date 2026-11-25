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

    [Fact]
    public async Task SimulatedRefundEmailsTheBuyerExactlyOnce()
    {
        using var coreScope = fixture.Core.Services.CreateScope();
        var options = coreScope.ServiceProvider.GetRequiredService<DbContextOptions<TicketShieldDbContext>>();
        Guid escrowId;
        await using (var db = new TicketShieldDbContext(options))
        {
            var escrow = await db.EscrowTransactions.OrderBy(e => e.CreatedAt).LastAsync();
            escrow.Status = EscrowStatus.RefundQueued;
            escrow.RecipientEmail = "refund-buyer@example.invalid";
            escrow.PaymentReference = "TSREFUND01";
            await db.SaveChangesAsync();
            escrowId = escrow.Id;
        }

        var emails = new RecordingEmailService();
        var services = new ServiceCollection();
        services.AddScoped<TicketShield.Application.Common.Interfaces.ITicketShieldDbContext>(_ => new TicketShieldDbContext(options));
        services.AddScoped<TicketShield.Application.Common.Interfaces.IEmailService>(_ => emails);
        var worker = new SimulatedRefundWorker(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SimulatedRefundWorker>.Instance);

        Assert.True(await worker.ProcessQueuedAsync(CancellationToken.None) >= 1);
        Assert.Equal(0, await worker.ProcessQueuedAsync(CancellationToken.None));

        var sent = Assert.Single(emails.Sent, m => m.To == "refund-buyer@example.invalid");
        Assert.Contains("TSREFUND01", sent.Body);
        await using var verify = new TicketShieldDbContext(options);
        Assert.Equal(EscrowStatus.Refunded, (await verify.EscrowTransactions.SingleAsync(e => e.Id == escrowId)).Status);
    }

    private sealed class RecordingEmailService : TicketShield.Application.Common.Interfaces.IEmailService
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            Sent.Add((toEmail, subject, htmlBody));
            return Task.CompletedTask;
        }
    }
}
