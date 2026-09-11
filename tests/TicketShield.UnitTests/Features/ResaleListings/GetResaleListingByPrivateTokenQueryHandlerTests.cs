using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingByPrivateToken;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class GetResaleListingByPrivateTokenQueryHandlerTests
{
    private static TicketShieldDbContext CreateInMemoryDbContext()
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
            TierName = "VIP Diamond",
            OriginalPrice = 5_000_000m,
            Event = testEvent
        };
        var seller = new User
        {
            Id = Guid.NewGuid(),
            Email = "seller_private@ticketshield.vn",
            FullName = "Nguyen Van Private Seller",
            Role = UserRole.User
        };

        context.Organizers.Add(organizer);
        context.Events.Add(testEvent);
        context.TicketTiers.Add(tier);
        context.Users.Add(seller);
        context.SaveChanges();

        return context;
    }

    [Fact]
    public async Task Handle_WhenPrivateTokenIsValid_ShouldReturnListingDetail()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();
        var seller = await dbContext.Users.FirstAsync();
        var shareToken = "a1b2c3d4e5f67890123456789abcdef0";

        var privateListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-SECRET-VIP-999",
            OriginalPrice = 5_000_000m,
            ResalePrice = 4_500_000m,
            IsPrivate = true,
            PrivateAccessToken = shareToken,
            ListingStatus = ListingStatus.Verified
        };
        dbContext.ResaleListings.Add(privateListing);
        await dbContext.SaveChangesAsync();

        var handler = new GetResaleListingByPrivateTokenQueryHandler(dbContext);
        var query = new GetResaleListingByPrivateTokenQuery(shareToken);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(privateListing.Id, result.Data.ListingId);
        Assert.Equal("V-Concert Hanoi 2026", result.Data.EventName);
        Assert.Equal("VIP Diamond", result.Data.TierName);
        Assert.Equal(4_500_000m, result.Data.ResalePrice);
        Assert.Equal(500_000m, result.Data.DiscountAmount);
        Assert.True(result.Data.IsPrivate);
        // Original ticket code masked
        Assert.NotEqual("TCK-SECRET-VIP-999", result.Data.MaskedTicketCode);
        Assert.Contains("*", result.Data.MaskedTicketCode);
    }

    [Fact]
    public async Task Handle_WhenPrivateTokenIsInvalidOrNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var handler = new GetResaleListingByPrivateTokenQueryHandler(dbContext);
        var query = new GetResaleListingByPrivateTokenQuery("non_existent_token_123");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(query, CancellationToken.None));
        Assert.Contains("Liên kết bán riêng tư không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task Handle_WhenTokenIsEmptyOrWhitespace_ShouldThrowNotFoundException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var handler = new GetResaleListingByPrivateTokenQueryHandler(dbContext);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetResaleListingByPrivateTokenQuery("   "), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenListingIsPublic_ShouldNotBeFoundByPrivateEndpoint()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();
        var seller = await dbContext.Users.FirstAsync();

        var publicListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-PUBLIC-123",
            OriginalPrice = 5_000_000m,
            ResalePrice = 4_500_000m,
            IsPrivate = false,
            PrivateAccessToken = "public_token_sample",
            ListingStatus = ListingStatus.Verified
        };
        dbContext.ResaleListings.Add(publicListing);
        await dbContext.SaveChangesAsync();

        var handler = new GetResaleListingByPrivateTokenQueryHandler(dbContext);
        var query = new GetResaleListingByPrivateTokenQuery("public_token_sample");

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(query, CancellationToken.None));
    }
}
