using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.ResaleListings.Commands.ReleaseListingHold;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class ReleaseListingHoldCommandHandlerTests
{
    private static (TicketShieldDbContext dbContext, ShadowUser seller, ShadowUser buyer1, ShadowUser buyer2) CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new TicketShieldDbContext(options);

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "V-Concert Organizer",
            OfficialEmail = "vconcert@test.com"
        };
        var testEvent = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "V-Concert Hanoi 2026",
            Venue = "Trung tam Hoi nghi Quoc gia",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(15),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(15).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(14),
            Organizer = organizer
        };
        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierName = "VIP",
            OriginalPrice = 2_000_000m,
            Event = testEvent
        };
        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller@ticketshield.vn",
            FullName = "Nguyen Van Seller"
        };
        var buyer1 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer1@ticketshield.vn",
            FullName = "Tran Van Buyer One"
        };
        var buyer2 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer2@ticketshield.vn",
            FullName = "Le Van Buyer Two"
        };

        context.Organizers.Add(organizer);
        context.Events.Add(testEvent);
        context.TicketTiers.Add(tier);
        context.ShadowUsers.AddRange(seller, buyer1, buyer2);
        context.SaveChanges();

        return (context, seller, buyer1, buyer2);
    }

    private class MockCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; }
        public string? Email => "buyer1@ticketshield.vn";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;

        public MockCurrentUserService(Guid? userId) => UserId = userId;
    }

    [Fact]
    public async Task Handle_ValidTransactingListingAndBuyer_ShouldReleaseHoldSuccessfully()
    {
        // Arrange
        var (dbContext, seller, buyer1, _) = CreateInMemoryDbContext();
        var currentUserService = new MockCurrentUserService(buyer1.Id);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-RELEASE-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            PaymentReference = "TSRELEASE01",
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(8)
        };
        listing.EscrowTransaction = escrow;
        dbContext.ResaleListings.Add(listing);
        dbContext.EscrowTransactions.Add(escrow);
        await dbContext.SaveChangesAsync();

        var handler = new ReleaseListingHoldCommandHandler(dbContext, currentUserService);
        var command = new ReleaseListingHoldCommand(listing.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Verified", result.Data.ListingStatus);

        var updatedListing = await dbContext.ResaleListings.FirstAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Verified, updatedListing.ListingStatus);
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotBuyerWhoHeld_ShouldThrowForbiddenAccessException()
    {
        // Arrange
        var (dbContext, seller, buyer1, buyer2) = CreateInMemoryDbContext();
        var currentUserService = new MockCurrentUserService(buyer2.Id); // Buyer2 tries to release Buyer1's hold

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-RELEASE-002",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            PaymentReference = "TSRELEASE02",
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(8)
        };
        listing.EscrowTransaction = escrow;
        dbContext.ResaleListings.Add(listing);
        dbContext.EscrowTransactions.Add(escrow);
        await dbContext.SaveChangesAsync();

        var handler = new ReleaseListingHoldCommandHandler(dbContext, currentUserService);
        var command = new ReleaseListingHoldCommand(listing.Id);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenListingIsNotTransacting_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var (dbContext, seller, buyer1, _) = CreateInMemoryDbContext();
        var currentUserService = new MockCurrentUserService(buyer1.Id);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-RELEASE-003",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified // Already Verified
        };
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        var handler = new ReleaseListingHoldCommandHandler(dbContext, currentUserService);
        var command = new ReleaseListingHoldCommand(listing.Id);

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var (dbContext, _, _, _) = CreateInMemoryDbContext();
        var currentUserService = new MockCurrentUserService(null);
        var handler = new ReleaseListingHoldCommandHandler(dbContext, currentUserService);

        var command = new ReleaseListingHoldCommand(Guid.NewGuid());

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenListingDoesNotExist_ShouldThrowNotFoundException()
    {
        // Arrange
        var (dbContext, _, buyer1, _) = CreateInMemoryDbContext();
        var currentUserService = new MockCurrentUserService(buyer1.Id);
        var handler = new ReleaseListingHoldCommandHandler(dbContext, currentUserService);

        var command = new ReleaseListingHoldCommand(Guid.NewGuid());

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }
}
