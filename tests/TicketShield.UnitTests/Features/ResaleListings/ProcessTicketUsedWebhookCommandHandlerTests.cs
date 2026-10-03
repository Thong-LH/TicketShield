using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessTicketUsedWebhook;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class ProcessTicketUsedWebhookCommandHandlerTests
{
    private sealed class RecordingSettler : IEscrowPayoutSettler
    {
        public Guid? SettledEscrowId { get; private set; }

        public Task<bool> TrySettleAsync(Guid escrowId, CancellationToken cancellationToken = default)
        {
            SettledEscrowId = escrowId;
            return Task.FromResult(true);
        }
    }

    private static (TicketShieldDbContext dbContext, EscrowTransaction escrow) CreateTestFixture(string? ticketCode = "TCK-12345")
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new TicketShieldDbContext(options);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            OriginalTicketCode = "OLD-TCK",
            ListingStatus = ListingStatus.Sold
        };

        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            NewTicketCode = ticketCode,
            Status = EscrowStatus.Locked,
            UnlockAt = DateTimeOffset.UtcNow.AddDays(1)
        };

        listing.EscrowTransaction = escrow;

        context.ResaleListings.Add(listing);
        context.EscrowTransactions.Add(escrow);
        context.SaveChanges();

        return (context, escrow);
    }

    [Fact]
    public async Task Handle_ValidTicketCode_ShouldSettleTheLockedEscrow()
    {
        var (context, escrow) = CreateTestFixture("VALID-TCK");
        var settler = new RecordingSettler();
        var handler = new ProcessTicketUsedWebhookCommandHandler(context, settler);

        var result = await handler.Handle(
            new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest { TicketCode = "VALID-TCK" }),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(escrow.Id, result.Data!.EscrowId);
        Assert.Equal(escrow.Id, settler.SettledEscrowId);
    }

    [Fact]
    public async Task Handle_MissingTicketCode_ShouldThrowBusinessRuleViolationException()
    {
        var (context, _) = CreateTestFixture();
        var handler = new ProcessTicketUsedWebhookCommandHandler(context, new RecordingSettler());

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(
            new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest { TicketCode = "" }),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NonExistentTicketCode_ShouldThrowNotFoundException()
    {
        var (context, _) = CreateTestFixture();
        var handler = new ProcessTicketUsedWebhookCommandHandler(context, new RecordingSettler());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest { TicketCode = "NON-EXISTENT" }),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_BundleTicketCode_ShouldSettleTheLockedEscrow()
    {
        var (context, escrow) = CreateTestFixture("TCK-1,TCK-2,TCK-3");
        var settler = new RecordingSettler();
        var handler = new ProcessTicketUsedWebhookCommandHandler(context, settler);

        var result = await handler.Handle(
            new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest { TicketCode = "TCK-2" }),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(escrow.Id, settler.SettledEscrowId);
    }

    [Fact]
    public async Task Handle_OriginalTicketCodeFallback_ShouldSettleTheLockedEscrow()
    {
        var (context, escrow) = CreateTestFixture(null);
        escrow.Listing.OriginalTicketCode = "OLD-TCK";
        await context.SaveChangesAsync();

        var settler = new RecordingSettler();
        var handler = new ProcessTicketUsedWebhookCommandHandler(context, settler);

        var result = await handler.Handle(
            new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest { TicketCode = "OLD-TCK" }),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(escrow.Id, settler.SettledEscrowId);
    }
}
