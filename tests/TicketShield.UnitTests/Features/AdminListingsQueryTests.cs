using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Features.Admin.Listings.Queries.GetAdminListings;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features;

public class AdminListingsQueryTests
{
    [Fact]
    public async Task Handle_DefaultQuery_ReturnsPaginatedListAndSummaryMetrics()
    {
        await using var db = CreateDb();
        await SeedSampleListingsAsync(db);

        var handler = new GetAdminListingsQueryHandler(db);
        var query = new GetAdminListingsQuery { Page = 1, Size = 10 };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(5, result.Data.Listings.TotalCount);
        Assert.Equal(5, result.Data.Listings.Items.Count);

        // Check Summary metrics
        Assert.Equal(5, result.Data.Summary.TotalListings);
        Assert.Equal(3, result.Data.Summary.TotalSingleTickets); // 3 single tickets
        Assert.Equal(2, result.Data.Summary.TotalBundles);       // 2 combo tickets in 1 bundle
        Assert.Equal(3, result.Data.Summary.TotalActiveListings); // 3 Verified
        Assert.Equal(2, result.Data.Summary.TotalSoldListings);   // 2 Sold
        Assert.Equal(2, result.Data.Summary.TotalAtCeilingListings); // 2 listings priced at max ceiling
    }

    [Fact]
    public async Task Handle_FilterByBundle_ReturnsOnlyComboBundles()
    {
        await using var db = CreateDb();
        await SeedSampleListingsAsync(db);

        var handler = new GetAdminListingsQueryHandler(db);
        var query = new GetAdminListingsQuery { IsBundleOnly = true };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Listings.TotalCount);
        Assert.All(result.Data.Listings.Items, item =>
        {
            Assert.True(item.IsBundle);
            Assert.NotNull(item.BundleId);
            Assert.Equal(2, item.BundleTotalTickets);
        });
    }

    [Fact]
    public async Task Handle_FilterBySingleOnly_ReturnsOnlySingleListings()
    {
        await using var db = CreateDb();
        await SeedSampleListingsAsync(db);

        var handler = new GetAdminListingsQueryHandler(db);
        var query = new GetAdminListingsQuery { IsSingleOnly = true };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(3, result.Data!.Listings.TotalCount);
        Assert.All(result.Data.Listings.Items, item =>
        {
            Assert.False(item.IsBundle);
            Assert.Null(item.BundleId);
        });
    }

    [Fact]
    public async Task Handle_PriceCeilingInspection_ComputesMaxAllowedPriceAndCeilingFlagsCorrectly()
    {
        await using var db = CreateDb();
        await SeedSampleListingsAsync(db);

        var handler = new GetAdminListingsQueryHandler(db);
        // Lấy tin niêm yết bán tại trần giá (OriginalPrice = 100_000, markup = 10%, ResalePrice = 110_000)
        var query = new GetAdminListingsQuery { Keyword = "TCK-AT-CEIL" };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        var listing = Assert.Single(result.Data!.Listings.Items);
        Assert.Equal(100_000m, listing.OriginalPrice);
        Assert.Equal(110_000m, listing.ResalePrice);
        Assert.Equal(10m, listing.AppliedMarkupPercentage);
        Assert.Equal(110_000m, listing.MaxAllowedPrice);
        Assert.True(listing.IsAtPriceCeiling);
        Assert.False(listing.IsPriceCeilingExceeded);
        Assert.Equal(10m, listing.ActualMarkupPercentage);
    }

    [Fact]
    public async Task Handle_FilterByIsAtPriceCeilingOnly_ReturnsOnlyCeilingListings()
    {
        await using var db = CreateDb();
        await SeedSampleListingsAsync(db);

        var handler = new GetAdminListingsQueryHandler(db);
        var query = new GetAdminListingsQuery { IsAtPriceCeilingOnly = true };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Listings.TotalCount);
        Assert.All(result.Data.Listings.Items, item => Assert.True(item.IsAtPriceCeiling));
    }

    [Fact]
    public async Task Handle_SearchKeyword_ReturnsMatchingRecords()
    {
        await using var db = CreateDb();
        await SeedSampleListingsAsync(db);

        var handler = new GetAdminListingsQueryHandler(db);

        // Search by Seller Email
        var querySeller = new GetAdminListingsQuery { Keyword = "seller.viet@test.local" };
        var resultSeller = await handler.Handle(querySeller, CancellationToken.None);
        Assert.True(resultSeller.Success);
        Assert.NotEmpty(resultSeller.Data!.Listings.Items);
        Assert.All(resultSeller.Data.Listings.Items, item => Assert.Equal("seller.viet@test.local", item.SellerEmail));

        // Search by Event Name
        var queryEvent = new GetAdminListingsQuery { Keyword = "Taylor Swift" };
        var resultEvent = await handler.Handle(queryEvent, CancellationToken.None);
        Assert.True(resultEvent.Success);
        Assert.Equal(3, resultEvent.Data!.Listings.TotalCount);
        Assert.All(resultEvent.Data.Listings.Items, item => Assert.Contains("Taylor Swift", item.EventName));
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPageAndMetadata()
    {
        await using var db = CreateDb();
        await SeedSampleListingsAsync(db);

        var handler = new GetAdminListingsQueryHandler(db);
        var query = new GetAdminListingsQuery { Page = 2, Size = 2 };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Listings.PageNumber);
        Assert.Equal(2, result.Data.Listings.PageSize);
        Assert.Equal(5, result.Data.Listings.TotalCount);
        Assert.Equal(3, result.Data.Listings.TotalPages);
        Assert.Equal(2, result.Data.Listings.Items.Count);
        Assert.True(result.Data.Listings.HasPreviousPage);
        Assert.True(result.Data.Listings.HasNextPage);
    }

    private static TicketShieldDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    private static async Task SeedSampleListingsAsync(TicketShieldDbContext db)
    {
        var org = new Organizer { Id = Guid.NewGuid(), Name = "TicketBox Org", OfficialEmail = "contact@ticketbox.local" };

        var seller1 = new ShadowUser { Id = Guid.NewGuid(), FullName = "Tran Quoc Viet", Email = "seller.viet@test.local", PhoneNumber = "0987654321" };
        var seller2 = new ShadowUser { Id = Guid.NewGuid(), FullName = "Hoang My Linh", Email = "seller.linh@test.local", PhoneNumber = "0912345678" };

        var event1 = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "Taylor Swift Eras Tour",
            Venue = "National Stadium",
            MaxResaleMarkupPercentage = 10m,
            EventStartAt = DateTimeOffset.UtcNow.AddDays(30),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(30).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(29)
        };

        var event2 = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "Ha Anh Tuan Sketch A Rose",
            Venue = "Hoa Binh Theater",
            MaxResaleMarkupPercentage = 15m,
            EventStartAt = DateTimeOffset.UtcNow.AddDays(15),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(15).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(14)
        };

        var tier1 = new TicketTier { Id = Guid.NewGuid(), EventId = event1.Id, TierName = "CAT 1", OriginalPrice = 100_000m };
        var tier2 = new TicketTier { Id = Guid.NewGuid(), EventId = event2.Id, TierName = "VIP", OriginalPrice = 200_000m };

        // 1. Single listing at price ceiling (Original = 100k, markup = 10%, Resale = 110k)
        var listing1 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event1.Id,
            TierId = tier1.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-AT-CEIL",
            OriginalPrice = 100_000m,
            ResalePrice = 110_000m,
            AppliedMarkupPercentage = 10m,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        // 2. Single listing below ceiling / discounted (Original = 100k, Resale = 90k)
        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event1.Id,
            TierId = tier1.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-DISCOUNT",
            OriginalPrice = 100_000m,
            ResalePrice = 90_000m,
            AppliedMarkupPercentage = 10m,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };

        // 3. Single listing Sold at price ceiling (Original = 200k, markup = 15%, Resale = 230k)
        var listing3 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event2.Id,
            TierId = tier2.Id,
            SellerId = seller2.Id,
            OriginalTicketCode = "TCK-HAT-SOLD",
            OriginalPrice = 200_000m,
            ResalePrice = 230_000m,
            AppliedMarkupPercentage = 15m,
            ListingStatus = ListingStatus.Sold,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-3)
        };

        // 4 & 5. Combo Bundle with 2 tickets (Sold together)
        var bundleId = Guid.NewGuid();
        var listing4 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            BundleId = bundleId,
            IsBundleAllOrNothing = true,
            BundleTotalTickets = 2,
            EventId = event1.Id,
            TierId = tier1.Id,
            SellerId = seller2.Id,
            OriginalTicketCode = "TCK-BDL-01",
            OriginalPrice = 100_000m,
            ResalePrice = 105_000m,
            AppliedMarkupPercentage = 10m,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-4)
        };

        var listing5 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            BundleId = bundleId,
            IsBundleAllOrNothing = true,
            BundleTotalTickets = 2,
            EventId = event2.Id,
            TierId = tier2.Id,
            SellerId = seller2.Id,
            OriginalTicketCode = "TCK-BDL-02",
            OriginalPrice = 200_000m,
            ResalePrice = 210_000m,
            AppliedMarkupPercentage = 15m,
            ListingStatus = ListingStatus.Sold,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-5)
        };

        db.Organizers.Add(org);
        db.ShadowUsers.AddRange(seller1, seller2);
        db.Events.AddRange(event1, event2);
        db.TicketTiers.AddRange(tier1, tier2);
        db.ResaleListings.AddRange(listing1, listing2, listing3, listing4, listing5);

        await db.SaveChangesAsync();
    }
}

