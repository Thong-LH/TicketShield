using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessTicketUsedWebhook;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class ProcessTicketUsedWebhookCommandHandlerTests
{
    private static (TicketShieldDbContext dbContext, EscrowTransaction escrow) CreateTestFixture(string ticketCode = "TCK-12345")
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
            NewTicketCode = ticketCode, // Webhook checks NewTicketCode first
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
    public async Task Handle_ValidTicketCode_ShouldSetUnlockAtToUtcNow()
    {
        // Arrange
        var (context, escrow) = CreateTestFixture("VALID-TCK");
        var handler = new ProcessTicketUsedWebhookCommandHandler(context);
        
        var request = new OrganizerTicketUsedWebhookRequest { TicketCode = "VALID-TCK" };
        var command = new ProcessTicketUsedWebhookCommand(request);
        
        var beforeUtc = DateTimeOffset.UtcNow;

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(escrow.Id, result.Data!.EscrowId);
        Assert.Equal(EscrowStatus.Locked.ToString(), result.Data.EscrowStatus);
        
        var dbEscrow = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.NotNull(dbEscrow);
        Assert.True(dbEscrow.UnlockAt <= DateTimeOffset.UtcNow && dbEscrow.UnlockAt >= beforeUtc);
    }

    [Fact]
    public async Task Handle_MissingTicketCode_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var (context, _) = CreateTestFixture();
        var handler = new ProcessTicketUsedWebhookCommandHandler(context);
        
        var request = new OrganizerTicketUsedWebhookRequest { TicketCode = "" };
        var command = new ProcessTicketUsedWebhookCommand(request);

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NonExistentTicketCode_ShouldThrowNotFoundException()
    {
        // Arrange
        var (context, _) = CreateTestFixture();
        var handler = new ProcessTicketUsedWebhookCommandHandler(context);
        
        var request = new OrganizerTicketUsedWebhookRequest { TicketCode = "NON-EXISTENT" };
        var command = new ProcessTicketUsedWebhookCommand(request);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_BundleTicketCode_ShouldSetUnlockAtToUtcNow()
    {
        // Arrange
        var (context, escrow) = CreateTestFixture("TCK-1,TCK-2,TCK-3");
        var handler = new ProcessTicketUsedWebhookCommandHandler(context);
        
        // MockOrganizer just sends one of the codes
        var request = new OrganizerTicketUsedWebhookRequest { TicketCode = "TCK-2" };
        var command = new ProcessTicketUsedWebhookCommand(request);
        
        var beforeUtc = DateTimeOffset.UtcNow;

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var dbEscrow = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.True(dbEscrow!.UnlockAt <= DateTimeOffset.UtcNow && dbEscrow.UnlockAt >= beforeUtc);
    }

    [Fact]
    public async Task Handle_OriginalTicketCodeFallback_ShouldSetUnlockAtToUtcNow()
    {
        // Arrange
        var (context, escrow) = CreateTestFixture(null!); // NewTicketCode is null
        escrow.Listing.OriginalTicketCode = "OLD-TCK";
        await context.SaveChangesAsync();
        
        var handler = new ProcessTicketUsedWebhookCommandHandler(context);
        
        var request = new OrganizerTicketUsedWebhookRequest { TicketCode = "OLD-TCK" };
        var command = new ProcessTicketUsedWebhookCommand(request);
        
        var beforeUtc = DateTimeOffset.UtcNow;

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var dbEscrow = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.True(dbEscrow!.UnlockAt <= DateTimeOffset.UtcNow && dbEscrow.UnlockAt >= beforeUtc);
    }
}
