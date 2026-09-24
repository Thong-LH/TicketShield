using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TicketShield.Contracts.Events;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Messaging.Consumers;
using TicketShield.Infrastructure.Persistence;
using Xunit;
using CoreEvent = TicketShield.Domain.Entities.Event;

namespace TicketShield.UnitTests.Messaging;

public class HoldExpiredConsumerTests
{
    private TicketShieldDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    [Fact]
    public async Task Consume_PendingEscrow_ShouldSetExpiredAndRevertListingToVerified()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();

        var ev = new CoreEvent
        {
            Id = Guid.NewGuid(),
            Name = "Future Concert",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        var tier = new TicketTier { Id = Guid.NewGuid(), EventId = ev.Id, TierName = "VIP" };
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = sellerId,
            ListingStatus = ListingStatus.Transacting,
            Event = ev,
            Tier = tier
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Listing = listing
        };

        dbContext.Events.Add(ev);
        dbContext.TicketTiers.Add(tier);
        dbContext.ResaleListings.Add(listing);
        dbContext.EscrowTransactions.Add(escrow);
        await dbContext.SaveChangesAsync();

        var consumer = new HoldExpiredConsumer(dbContext, NullLogger<HoldExpiredConsumer>.Instance);

        var mockMessage = new Mock<IHoldExpiredEvent>();
        mockMessage.Setup(m => m.EscrowId).Returns(escrow.Id);
        mockMessage.Setup(m => m.ListingId).Returns(listing.Id);

        var mockContext = new Mock<ConsumeContext<IHoldExpiredEvent>>();
        mockContext.Setup(c => c.Message).Returns(mockMessage.Object);
        mockContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await consumer.Consume(mockContext.Object);

        // Assert
        var updatedEscrow = await dbContext.EscrowTransactions.FindAsync(escrow.Id);
        var updatedListing = await dbContext.ResaleListings.FindAsync(listing.Id);

        Assert.NotNull(updatedEscrow);
        Assert.Equal(EscrowStatus.Expired, updatedEscrow.Status);
        Assert.NotNull(updatedListing);
        Assert.Equal(ListingStatus.Verified, updatedListing.ListingStatus);
    }

    [Fact]
    public async Task Consume_EventPastCutoff_ShouldSetListingCancelled()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();

        var ev = new CoreEvent
        {
            Id = Guid.NewGuid(),
            Name = "Past Concert",
            EventStartAt = DateTimeOffset.UtcNow.AddHours(1) // less than 2 hours to event!
        };
        var tier = new TicketTier { Id = Guid.NewGuid(), EventId = ev.Id, TierName = "VIP" };
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = sellerId,
            ListingStatus = ListingStatus.Transacting,
            Event = ev,
            Tier = tier
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Listing = listing
        };

        dbContext.Events.Add(ev);
        dbContext.TicketTiers.Add(tier);
        dbContext.ResaleListings.Add(listing);
        dbContext.EscrowTransactions.Add(escrow);
        await dbContext.SaveChangesAsync();

        var consumer = new HoldExpiredConsumer(dbContext, NullLogger<HoldExpiredConsumer>.Instance);

        var mockMessage = new Mock<IHoldExpiredEvent>();
        mockMessage.Setup(m => m.EscrowId).Returns(escrow.Id);
        mockMessage.Setup(m => m.ListingId).Returns(listing.Id);

        var mockContext = new Mock<ConsumeContext<IHoldExpiredEvent>>();
        mockContext.Setup(c => c.Message).Returns(mockMessage.Object);
        mockContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await consumer.Consume(mockContext.Object);

        // Assert
        var updatedEscrow = await dbContext.EscrowTransactions.FindAsync(escrow.Id);
        var updatedListing = await dbContext.ResaleListings.FindAsync(listing.Id);

        Assert.NotNull(updatedEscrow);
        Assert.Equal(EscrowStatus.Expired, updatedEscrow.Status);
        Assert.NotNull(updatedListing);
        Assert.Equal(ListingStatus.Expired, updatedListing.ListingStatus);
    }
}
