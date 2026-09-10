using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Features.ResaleListings.Commands.CreateResaleListing;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class CreateResaleListingCommandHandlerTests
{
    private static TicketShieldDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new TicketShieldDbContext(options);

        // Seed initial event, tier, seller
        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "Test Organizer",
            OfficialEmail = "organizer@test.com"
        };
        var testEvent = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "Test Concert 2026",
            Venue = "My Dinh Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(10),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(4),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(9),
            Organizer = organizer
        };
        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierName = "VIP Zone",
            OriginalPrice = 2_000_000m,
            Event = testEvent
        };
        var seller = new User
        {
            Id = Guid.NewGuid(),
            Email = "seller@ticketshield.vn",
            FullName = "Nguyen Van Seller",
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
    public async Task Handle_WhenPublicMode_ShouldCreateListingWithoutPrivateToken()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var handler = new CreateResaleListingCommandHandler(dbContext);
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();
        var seller = await dbContext.Users.FirstAsync();

        var command = new CreateResaleListingCommand
        {
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-PUBLIC-001",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_800_000m,
            IsPrivate = false
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsPrivate);
        Assert.Null(result.Data.PrivateAccessToken);
        Assert.Null(result.Data.ShareUrl);

        var dbListing = await dbContext.ResaleListings.FirstOrDefaultAsync(l => l.Id == result.Data.ListingId);
        Assert.NotNull(dbListing);
        Assert.False(dbListing.IsPrivate);
        Assert.Null(dbListing.PrivateAccessToken);
        Assert.Equal(ListingStatus.Verified, dbListing.ListingStatus);
    }

    [Fact]
    public async Task Handle_WhenPrivateMode_ShouldGeneratePrivateTokenAndShareUrl()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var handler = new CreateResaleListingCommandHandler(dbContext);
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();
        var seller = await dbContext.Users.FirstAsync();

        var command = new CreateResaleListingCommand
        {
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-PRIVATE-001",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_900_000m,
            IsPrivate = true
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsPrivate);
        Assert.False(string.IsNullOrWhiteSpace(result.Data.PrivateAccessToken));
        Assert.StartsWith("https://ticketshield.vn/p/", result.Data.ShareUrl!);

        var dbListing = await dbContext.ResaleListings.FirstOrDefaultAsync(l => l.Id == result.Data.ListingId);
        Assert.NotNull(dbListing);
        Assert.True(dbListing.IsPrivate);
        Assert.Equal(result.Data.PrivateAccessToken, dbListing.PrivateAccessToken);
    }

    [Fact]
    public async Task Handle_WhenTicketCodeAlreadyHasActiveListing_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var handler = new CreateResaleListingCommandHandler(dbContext);
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();
        var seller = await dbContext.Users.FirstAsync();

        // Create first active listing
        var existingListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-DUPLICATE-001",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Verified
        };
        dbContext.ResaleListings.Add(existingListing);
        await dbContext.SaveChangesAsync();

        var command = new CreateResaleListingCommand
        {
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-DUPLICATE-001",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_400_000m,
            IsPrivate = false
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => handler.Handle(command, CancellationToken.None));
        Assert.Contains("hiện đang được niêm yết bán", exception.Message);
    }
}
