using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.ResaleListings.Queries.GetSellerListings;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class GetSellerListingsQueryHandlerTests
{
    private static (TicketShieldDbContext dbContext, User seller1, User seller2) CreateInMemoryDbContext()
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
        var seller1 = new User
        {
            Id = Guid.NewGuid(),
            Email = "seller1@ticketshield.vn",
            FullName = "Nguyen Van Seller One",
            Role = UserRole.User
        };
        var seller2 = new User
        {
            Id = Guid.NewGuid(),
            Email = "seller2@ticketshield.vn",
            FullName = "Le Van Seller Two",
            Role = UserRole.User
        };

        context.Organizers.Add(organizer);
        context.Events.Add(testEvent);
        context.TicketTiers.Add(tier);
        context.Users.AddRange(seller1, seller2);
        context.SaveChanges();

        return (context, seller1, seller2);
    }

    private class MockCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; }
        public string? Email => "seller@ticketshield.vn";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;

        public MockCurrentUserService(Guid? userId) => UserId = userId;
    }

    [Fact]
    public async Task Handle_WhenSellerHasListings_ShouldReturnAllSellerListings()
    {
        // Arrange
        var (dbContext, seller1, seller2) = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();

        var listing1 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-SELLER1-001",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_800_000m,
            IsPrivate = false,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-SELLER1-002",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_500_000m,
            IsPrivate = true,
            PrivateAccessToken = "secret_token_12345678901234567890",
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var otherSellerListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller2.Id,
            OriginalTicketCode = "TCK-SELLER2-999",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_900_000m,
            IsPrivate = false,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ResaleListings.AddRange(listing1, listing2, otherSellerListing);
        await dbContext.SaveChangesAsync();

        var currentUserService = new MockCurrentUserService(seller1.Id);
        var handler = new GetSellerListingsQueryHandler(dbContext, currentUserService);
        var query = new GetSellerListingsQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Count);
        Assert.Equal("TCK-SELLER1-002", result.Data[0].OriginalTicketCode); // Ordered by CreatedAt Descending
        Assert.True(result.Data[0].IsPrivate);
        Assert.Equal("secret_token_12345678901234567890", result.Data[0].PrivateAccessToken);
        Assert.NotNull(result.Data[0].ShareUrl);
    }

    [Fact]
    public async Task Handle_WithStatusFilter_ShouldFilterListingsByStatus()
    {
        // Arrange
        var (dbContext, seller1, _) = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();

        var activeListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-ACTIVE-001",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_800_000m,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var cancelledListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-CANCELLED-002",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Cancelled,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ResaleListings.AddRange(activeListing, cancelledListing);
        await dbContext.SaveChangesAsync();

        var currentUserService = new MockCurrentUserService(seller1.Id);
        var handler = new GetSellerListingsQueryHandler(dbContext, currentUserService);

        // Act
        var resultActive = await handler.Handle(new GetSellerListingsQuery(ListingStatus.Verified), CancellationToken.None);
        var resultCancelled = await handler.Handle(new GetSellerListingsQuery(ListingStatus.Cancelled), CancellationToken.None);

        // Assert
        Assert.Single(resultActive.Data!);
        Assert.Equal("TCK-ACTIVE-001", resultActive.Data![0].OriginalTicketCode);

        Assert.Single(resultCancelled.Data!);
        Assert.Equal("TCK-CANCELLED-002", resultCancelled.Data![0].OriginalTicketCode);
    }

    [Fact]
    public async Task Handle_WhenSellerHasNoListings_ShouldReturnEmptyList()
    {
        // Arrange
        var (dbContext, seller1, _) = CreateInMemoryDbContext();
        var currentUserService = new MockCurrentUserService(seller1.Id);
        var handler = new GetSellerListingsQueryHandler(dbContext, currentUserService);
        var query = new GetSellerListingsQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }
}
