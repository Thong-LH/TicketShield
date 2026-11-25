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

        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller@ticketshield.vn",
            FullName = "Nguyen Van Seller"
        };

        context.Organizers.Add(organizer);
        context.Events.AddRange(testEvent, secondEvent);
        context.TicketTiers.AddRange(tier1, tier2);
        context.ShadowUsers.Add(seller);

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
        Assert.Equal(2, result.Data.Items.Count);
        Assert.Equal(2, result.Data.TotalCount);
        Assert.Equal(1, result.Data.PageNumber);
        Assert.Equal(10, result.Data.PageSize);
        Assert.Equal(1, result.Data.TotalPages);
        Assert.False(result.Data.HasNextPage);
        Assert.False(result.Data.HasPreviousPage);
        Assert.All(result.Data.Items, item =>
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
    public async Task Handle_UsesTheLatestActiveEscrowExpiry_ForTheMarketplaceCountdown()
    {
        var (context, _, secondEvent) = CreateInMemoryDbContext();
        var listing = context.ResaleListings.Single(l => l.EventId == secondEvent.Id);
        var seller = context.ShadowUsers.Single();
        var buyer = new ShadowUser { Id = Guid.NewGuid(), Email = "buyer@test.com", FullName = "Buyer" };
        var now = DateTimeOffset.UtcNow;

        // The later-created row has the shorter stale expiry. The UI must use
        // the active hold that actually expires last, matching checkout.
        context.ShadowUsers.Add(buyer);
        context.EscrowTransactions.AddRange(
            new EscrowTransaction
            {
                ListingId = listing.Id,
                BuyerId = buyer.Id,
                SellerId = seller.Id,
                Status = EscrowStatus.Pending,
                UnlockAt = now.AddMinutes(10),
                CreatedAt = now.AddMinutes(-2)
            },
            new EscrowTransaction
            {
                ListingId = listing.Id,
                BuyerId = buyer.Id,
                SellerId = seller.Id,
                Status = EscrowStatus.Pending,
                UnlockAt = now.AddMinutes(4),
                CreatedAt = now.AddMinutes(-1)
            });
        await context.SaveChangesAsync();

        var result = await new GetMarketplaceListingsQueryHandler(context)
            .Handle(new GetMarketplaceListingsQuery(page: 1, size: 10), CancellationToken.None);

        var dto = Assert.Single(result.Data!.Items, item => item.ListingId == listing.Id);
        Assert.NotNull(dto.UnlockAt);
        Assert.True(dto.UnlockAt.Value > now.AddMinutes(9));
    }

    [Fact]
    public async Task Handle_WithEventIdFilter_ReturnsMatchingListings()
    {
        var (context, testEvent, _) = CreateInMemoryDbContext();
        var handler = new GetMarketplaceListingsQueryHandler(context);
        var query = new GetMarketplaceListingsQuery(page: 1, size: 10, eventId: testEvent.Id);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.NotNull(result?.Data);
        Assert.Single(result.Data.Items);
        Assert.Equal(1, result.Data.TotalCount);
        Assert.Equal(testEvent.Id, result.Data.Items[0].EventId);
        Assert.Equal("Anh Trai Say Hi Concert 2026", result.Data.Items[0].EventName);
    }

    [Fact]
    public async Task Handle_WithKeywordFilter_ReturnsMatchingListings()
    {
        var (context, _, _) = CreateInMemoryDbContext();
        var handler = new GetMarketplaceListingsQueryHandler(context);
        var query = new GetMarketplaceListingsQuery(page: 1, size: 10, keyword: "Say Hi");

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.NotNull(result?.Data);
        Assert.Single(result.Data.Items);
        Assert.Equal(1, result.Data.TotalCount);
        Assert.Equal("Anh Trai Say Hi Concert 2026", result.Data.Items[0].EventName);
    }

    // TC-MKT-01 / TC-MKT-03: filters, sort and the total must cover every listing, not only the current page.
    private static (TicketShieldDbContext Context, Guid SportsListingId) CreateManyListingsAcrossPages()
    {
        var (context, testEvent, _) = CreateInMemoryDbContext();
        var organizer = context.Organizers.Single();
        var seller = context.ShadowUsers.Single();
        var derby = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "Tran Derby: CLB Ha Noi vs CLB Viettel",
            Venue = "San van dong Hang Day",
            Category = "SPORTS",
            City = "Hà Nội",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(3),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(3).AddHours(2),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(2),
            Organizer = organizer
        };
        var derbyTier = new TicketTier { Id = Guid.NewGuid(), EventId = derby.Id, TierName = "Khan dai B", OriginalPrice = 200_000m, Event = derby };
        context.Events.Add(derby);
        context.TicketTiers.Add(derbyTier);
        var tier = context.TicketTiers.Single(t => t.EventId == testEvent.Id);
        for (var i = 0; i < 12; i++)
        {
            context.ResaleListings.Add(new ResaleListing
            {
                Id = Guid.NewGuid(), EventId = testEvent.Id, TierId = tier.Id, SellerId = seller.Id,
                OriginalTicketCode = $"AT-PAGE-{i:00}", OriginalPrice = 1_500_000m, ResalePrice = 1_000_000m + i * 1_000m,
                ListingStatus = ListingStatus.Verified, VerificationStatus = VerificationStatus.Verified,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(i), Event = testEvent, Tier = tier, Seller = seller
            });
        }
        // The oldest listing, so it lands on the last page when sorted by "newest".
        var sports = new ResaleListing
        {
            Id = Guid.NewGuid(), EventId = derby.Id, TierId = derbyTier.Id, SellerId = seller.Id,
            OriginalTicketCode = "DERBY-001", OriginalPrice = 200_000m, ResalePrice = 150_000m,
            ListingStatus = ListingStatus.Verified, VerificationStatus = VerificationStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3), Event = derby, Tier = derbyTier, Seller = seller
        };
        context.ResaleListings.Add(sports);
        context.SaveChanges();
        return (context, sports.Id);
    }

    [Fact]
    public async Task Handle_WithCategoryFilter_FindsListingsOutsideTheFirstPage()
    {
        var (context, sportsId) = CreateManyListingsAcrossPages();
        var handler = new GetMarketplaceListingsQueryHandler(context);

        var result = await handler.Handle(new GetMarketplaceListingsQuery(page: 1, size: 12) { Category = "SPORTS" }, CancellationToken.None);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(sportsId, item.ListingId);
        Assert.Equal(1, result.Data.TotalCount);
    }

    [Fact]
    public async Task Handle_WithCityFilter_MatchesTheEventCityByCode()
    {
        var (context, sportsId) = CreateManyListingsAcrossPages();
        var handler = new GetMarketplaceListingsQueryHandler(context);

        var hanoi = await handler.Handle(new GetMarketplaceListingsQuery(page: 1, size: 12) { City = "HN" }, CancellationToken.None);
        var daNang = await handler.Handle(new GetMarketplaceListingsQuery(page: 1, size: 12) { City = "DN" }, CancellationToken.None);

        Assert.Equal(sportsId, Assert.Single(hanoi.Data!.Items).ListingId);
        Assert.Empty(daNang.Data!.Items);
    }

    [Fact]
    public async Task Handle_WithUpcomingDateRange_KeepsOnlyEventsInTheNextSevenDays()
    {
        var (context, sportsId) = CreateManyListingsAcrossPages();
        var handler = new GetMarketplaceListingsQueryHandler(context);

        var result = await handler.Handle(new GetMarketplaceListingsQuery(page: 1, size: 12) { DateRange = "upcoming" }, CancellationToken.None);

        Assert.Equal(sportsId, Assert.Single(result.Data!.Items).ListingId);
    }

    [Fact]
    public async Task Handle_SortedByPrice_StartsWithTheCheapestListingOfAllPages()
    {
        var (context, sportsId) = CreateManyListingsAcrossPages();
        var handler = new GetMarketplaceListingsQueryHandler(context);

        var result = await handler.Handle(new GetMarketplaceListingsQuery(page: 1, size: 12) { SortBy = "price-asc" }, CancellationToken.None);

        Assert.Equal(sportsId, result.Data!.Items[0].ListingId);
        Assert.Equal(result.Data.Items.Select(i => i.ResalePrice).OrderBy(p => p), result.Data.Items.Select(i => i.ResalePrice));
        Assert.Equal(15, result.Data.TotalCount); // 2 base + 12 page listings + 1 sports
    }

    [Fact]
    public async Task Handle_WithOrganizerIdFilter_ReturnsMatchingListingsAndOrganizerName()
    {
        var (context, testEvent, _) = CreateInMemoryDbContext();
        var handler = new GetMarketplaceListingsQueryHandler(context);
        var query = new GetMarketplaceListingsQuery(page: 1, size: 10, organizerId: testEvent.OrganizerId);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.NotNull(result?.Data);
        Assert.True(result.Data.Items.Count > 0);
        var firstItem = result.Data.Items[0];
        Assert.Equal(testEvent.OrganizerId, firstItem.OrganizerId);
        Assert.Equal("V-Concert Organizer", firstItem.OrganizerName);
    }
}
