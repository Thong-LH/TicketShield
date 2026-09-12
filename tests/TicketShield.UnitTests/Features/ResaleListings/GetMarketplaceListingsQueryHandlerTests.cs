using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Features.ResaleListings.Queries.GetMarketplaceListings;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class GetMarketplaceListingsQueryHandlerTests
{
    private static (TicketShieldDbContext Context, Event TestEvent, Event SecondEvent) CreateInMemoryDbContext()
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
            Name = "Anh Trai Say Hi Concert 2026",
            Venue = "San Van Dong My Dinh",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(15),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(15).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(14),
            Organizer = organizer
        };
        var secondEvent = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "Chi Dep Dap Gio 2026",
            Venue = "Nha Thi Dau Phu Tho",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(20),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(20).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(19),
            Organizer = organizer
        };

        var tier1 = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierName = "VIP Zone A",
            OriginalPrice = 1_500_000m,
            Event = testEvent
        };
        var tier2 = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = secondEvent.Id,
            TierName = "GA Standard",
            OriginalPrice = 800_000m,
            Event = secondEvent
        };

        var seller = new User
        {
            Id = Guid.NewGuid(),
            Email = "seller@ticketshield.vn",
            FullName = "Nguyen Van Seller",
            Role = UserRole.User
        };

        context.Organizers.Add(organizer);
        context.Events.AddRange(testEvent, secondEvent);
        context.TicketTiers.AddRange(tier1, tier2);
        context.Users.Add(seller);

        // 1. Verified Public Listing (Event 1)
        context.ResaleListings.Add(new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = tier1.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "AT-VIP-001",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_350_000m,
            IsPrivate = false,
            VerificationStatus = VerificationStatus.Verified,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            Event = testEvent,
            Tier = tier1,
            Seller = seller
        });

        // 2. Transacting Public Listing (Event 2)
        context.ResaleListings.Add(new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = secondEvent.Id,
            TierId = tier2.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "CD-GA-002",
            OriginalPrice = 800_000m,
            ResalePrice = 750_000m,
            IsPrivate = false,
            VerificationStatus = VerificationStatus.Verified,
            ListingStatus = ListingStatus.Transacting,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Event = secondEvent,
            Tier = tier2,
            Seller = seller
        });

        // 3. Private Listing (Should be excluded)
        context.ResaleListings.Add(new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = tier1.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "AT-VIP-PRIV",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_200_000m,
            IsPrivate = true,
            PrivateAccessToken = "secret-token",
            VerificationStatus = VerificationStatus.Verified,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow,
            Event = testEvent,
            Tier = tier1,
            Seller = seller
        });

        // 4. Cancelled Listing (Should be excluded)
        context.ResaleListings.Add(new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = tier1.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "AT-VIP-CANC",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_200_000m,
            IsPrivate = false,
            VerificationStatus = VerificationStatus.Verified,
            ListingStatus = ListingStatus.Cancelled,
            CreatedAt = DateTimeOffset.UtcNow,
            Event = testEvent,
            Tier = tier1,
            Seller = seller
        });

        context.SaveChanges();

        return (context, testEvent, secondEvent);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyPublicVerifiedAndTransactingListings()
    {
        var (context, _, _) = CreateInMemoryDbContext();
        var handler = new GetMarketplaceListingsQueryHandler(context);
        var query = new GetMarketplaceListingsQuery(page: 1, size: 10);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Count);
        Assert.All(result.Data, item =>
        {
            Assert.False(item.IsPrivate);
            Assert.Contains(item.ListingStatus, new[] { "Verified", "Transacting" });
            Assert.NotEmpty(item.EventName);
            Assert.NotEmpty(item.EventVenue);
            Assert.NotEmpty(item.TierName);
            Assert.NotEmpty(item.SellerFullName);
        });
    }

    [Fact]
    public async Task Handle_WithEventIdFilter_ReturnsMatchingListings()
    {
        var (context, testEvent, _) = CreateInMemoryDbContext();
        var handler = new GetMarketplaceListingsQueryHandler(context);
        var query = new GetMarketplaceListingsQuery(page: 1, size: 10, eventId: testEvent.Id);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.NotNull(result?.Data);
        Assert.Single(result.Data);
        Assert.Equal(testEvent.Id, result.Data[0].EventId);
        Assert.Equal("Anh Trai Say Hi Concert 2026", result.Data[0].EventName);
    }

    [Fact]
    public async Task Handle_WithKeywordFilter_ReturnsMatchingListings()
    {
        var (context, _, _) = CreateInMemoryDbContext();
        var handler = new GetMarketplaceListingsQueryHandler(context);
        var query = new GetMarketplaceListingsQuery(page: 1, size: 10, keyword: "Say Hi");

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.NotNull(result?.Data);
        Assert.Single(result.Data);
        Assert.Equal("Anh Trai Say Hi Concert 2026", result.Data[0].EventName);
    }
}
