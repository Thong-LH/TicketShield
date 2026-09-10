using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class GetResaleListingDetailQueryHandlerTests
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
            TierName = "Platinum",
            OriginalPrice = 3_000_000m,
            Event = testEvent
        };
        var seller = new User
        {
            Id = Guid.NewGuid(),
            Email = "seller2@ticketshield.vn",
            FullName = "Tran Thi Seller",
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
    public async Task Handle_WhenListingIsPublic_ShouldAllowAccessWithoutToken()
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
            OriginalTicketCode = "TCK-PUBLIC-SECRET-999",
            OriginalPrice = 3_000_000m,
            ResalePrice = 2_700_000m,
            IsPrivate = false,
            PrivateAccessToken = null,
            ListingStatus = ListingStatus.Verified
        };
        dbContext.ResaleListings.Add(publicListing);
        await dbContext.SaveChangesAsync();

        var handler = new GetResaleListingDetailQueryHandler(dbContext);
        var query = new GetResaleListingDetailQuery(publicListing.Id, token: null);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("V-Concert Hanoi 2026", result.Data.EventName);
        Assert.Equal("Platinum", result.Data.TierName);
        Assert.Equal(2_700_000m, result.Data.ResalePrice);
        Assert.False(result.Data.IsPrivate);
        // Ensure OriginalTicketCode is masked (prevent barcode theft)
        Assert.NotEqual("TCK-PUBLIC-SECRET-999", result.Data.MaskedTicketCode);
        Assert.Contains("*", result.Data.MaskedTicketCode);
    }

    [Fact]
    public async Task Handle_WhenListingIsPrivateAndTokenIsCorrect_ShouldAllowAccess()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();
        var seller = await dbContext.Users.FirstAsync();
        var secretToken = "a1b2c3d4e5f67890123456789abcdef0";

        var privateListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-PRIVATE-VIP-888",
            OriginalPrice = 3_000_000m,
            ResalePrice = 2_500_000m,
            IsPrivate = true,
            PrivateAccessToken = secretToken,
            ListingStatus = ListingStatus.Verified
        };
        dbContext.ResaleListings.Add(privateListing);
        await dbContext.SaveChangesAsync();

        var handler = new GetResaleListingDetailQueryHandler(dbContext);
        var query = new GetResaleListingDetailQuery(privateListing.Id, token: secretToken);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsPrivate);
        Assert.Equal(2_500_000m, result.Data.ResalePrice);
        Assert.Equal(500_000m, result.Data.DiscountAmount);
    }

    [Fact]
    public async Task Handle_WhenListingIsPrivateAndTokenIsMissingOrWrong_ShouldThrowForbiddenAccessException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();
        var seller = await dbContext.Users.FirstAsync();

        var privateListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-PRIVATE-VIP-777",
            OriginalPrice = 3_000_000m,
            ResalePrice = 2_800_000m,
            IsPrivate = true,
            PrivateAccessToken = "valid_secret_token_123",
            ListingStatus = ListingStatus.Verified
        };
        dbContext.ResaleListings.Add(privateListing);
        await dbContext.SaveChangesAsync();

        var handler = new GetResaleListingDetailQueryHandler(dbContext);

        // 1. Missing token
        var queryNoToken = new GetResaleListingDetailQuery(privateListing.Id, token: null);
        var exNoToken = await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => handler.Handle(queryNoToken, CancellationToken.None));
        Assert.Contains("Bạn không có quyền truy cập vé riêng tư này", exNoToken.Message);

        // 2. Wrong token
        var queryWrongToken = new GetResaleListingDetailQuery(privateListing.Id, token: "wrong_hacker_token");
        var exWrongToken = await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => handler.Handle(queryWrongToken, CancellationToken.None));
        Assert.Contains("Bạn không có quyền truy cập vé riêng tư này", exWrongToken.Message);
    }

    [Fact]
    public async Task Handle_WhenListingNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var handler = new GetResaleListingDetailQueryHandler(dbContext);
        var nonExistentId = Guid.NewGuid();
        var query = new GetResaleListingDetailQuery(nonExistentId);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(query, CancellationToken.None));
    }
}
