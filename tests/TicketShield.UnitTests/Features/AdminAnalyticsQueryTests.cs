using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Features.Admin.Analytics.Queries.GetAdminPlatformAnalytics;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features;

public class AdminAnalyticsQueryTests
{
    [Fact]
    public async Task Handle_PlatformRevenue_CalculatesAccurateFeeRevenueAndGmv()
    {
        await using var db = CreateDb();
        await SeedSampleAnalyticsDataAsync(db);

        var handler = new GetAdminPlatformAnalyticsQueryHandler(db);
        var query = new GetAdminPlatformAnalyticsQuery();

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        var revenue = result.Data.Revenue;
        // Released escrow: escrow1 (BuyerFee: 5_000, SellerFee: 5_000, TotalBuyerPaid: 105_000)
        Assert.Equal(5_000m, revenue.TotalBuyerFees);
        Assert.Equal(5_000m, revenue.TotalSellerFees);
        Assert.Equal(10_000m, revenue.TotalNetRevenue);
        Assert.Equal(105_000m, revenue.TotalGmv);

        // Locked escrow: escrow2 (BuyerFee: 7_500, SellerFee: 7_500 => Pending fees = 15_000)
        Assert.Equal(15_000m, revenue.PendingFeesInEscrow);

        // Refunded escrow: escrow3 (TotalBuyerPaid: 110_000)
        Assert.Equal(110_000m, revenue.TotalRefundedAmount);
    }

    [Fact]
    public async Task Handle_UserAnalytics_CountsBuyersSellersAndDualRoleCorrectly()
    {
        await using var db = CreateDb();
        await SeedSampleAnalyticsDataAsync(db);

        var handler = new GetAdminPlatformAnalyticsQueryHandler(db);
        var query = new GetAdminPlatformAnalyticsQuery();

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        var users = result.Data!.Users;

        // Total users = 3 (userA, userB, userC)
        Assert.Equal(3, users.TotalUsers);
        Assert.Equal(3, users.ActiveUsers);

        // userA bought escrow1 -> Buyer
        // userB bought escrow2 and listed listing1 -> Dual Role (Buyer & Seller)
        // userC listed listing2 -> Seller
        Assert.Equal(2, users.TotalBuyers); // userA, userB
        Assert.Equal(2, users.TotalSellers); // userB, userC
        Assert.Equal(1, users.DualRoleUsers); // userB
    }

    [Fact]
    public async Task Handle_TicketInventoryAnalytics_CountsTicketStatusesAccurately()
    {
        await using var db = CreateDb();
        await SeedSampleAnalyticsDataAsync(db);

        var handler = new GetAdminPlatformAnalyticsQueryHandler(db);
        var query = new GetAdminPlatformAnalyticsQuery();

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        var inventory = result.Data!.Inventory;

        Assert.Equal(5, inventory.TotalTicketsInInventory);
        Assert.Equal(1, inventory.ValidTickets);
        Assert.Equal(1, inventory.LockedForResaleTickets);
        Assert.Equal(1, inventory.TransferredTickets);
        Assert.Equal(1, inventory.UsedTickets);
        Assert.Equal(1, inventory.CancelledTickets);
    }

    [Fact]
    public async Task Handle_EventTransactionAnalytics_GroupsByEventCorrectly()
    {
        await using var db = CreateDb();
        await SeedSampleAnalyticsDataAsync(db);

        var handler = new GetAdminPlatformAnalyticsQueryHandler(db);
        var query = new GetAdminPlatformAnalyticsQuery();

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        var eventAnalytics = result.Data!.EventTransactions;

        Assert.NotEmpty(eventAnalytics);
        var event1Stats = eventAnalytics.FirstOrDefault(e => e.EventName == "Monsoon Music Festival");
        Assert.NotNull(event1Stats);
        Assert.Equal(2, event1Stats.TotalListings);
        Assert.Equal(1, event1Stats.TotalTicketsSold);
        Assert.Equal(1, event1Stats.TotalTicketsAvailable);
        Assert.Equal(105_000m, event1Stats.TotalTransactionVolume);
        Assert.Equal(10_000m, event1Stats.TotalPlatformRevenue);
    }

    [Fact]
    public async Task Handle_DateRangeFilter_ReturnsFilteredAnalytics()
    {
        await using var db = CreateDb();
        await SeedSampleAnalyticsDataAsync(db);

        var handler = new GetAdminPlatformAnalyticsQueryHandler(db);
        // Lọc ngày trong tương lai (không có giao dịch nào)
        var queryFuture = new GetAdminPlatformAnalyticsQuery
        {
            FromDate = DateTimeOffset.UtcNow.AddDays(10),
            ToDate = DateTimeOffset.UtcNow.AddDays(20)
        };

        var resultFuture = await handler.Handle(queryFuture, CancellationToken.None);

        Assert.True(resultFuture.Success);
        Assert.Equal(0m, resultFuture.Data!.Revenue.TotalNetRevenue);
        Assert.Equal(0, resultFuture.Data.Users.TotalBuyers);
        Assert.Equal(0, resultFuture.Data.Users.TotalSellers);
    }

    private static TicketShieldDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    private static async Task SeedSampleAnalyticsDataAsync(TicketShieldDbContext db)
    {
        var org = new Organizer { Id = Guid.NewGuid(), Name = "Monsoon Org", OfficialEmail = "contact@monsoon.local" };

        var userA = new ShadowUser { Id = Guid.NewGuid(), FullName = "User A (Buyer Only)", Email = "buyer.a@test.local", IsActive = true };
        var userB = new ShadowUser { Id = Guid.NewGuid(), FullName = "User B (Dual Role)", Email = "dual.b@test.local", IsActive = true };
        var userC = new ShadowUser { Id = Guid.NewGuid(), FullName = "User C (Seller Only)", Email = "seller.c@test.local", IsActive = true };

        var event1 = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "Monsoon Music Festival",
            Venue = "Thang Long Imperial Citadel",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(20),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(20).AddHours(5),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(19)
        };

        var event2 = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "Genfest 2026",
            Venue = "The Global City",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(40),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(40).AddHours(5),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(39)
        };

        var tier1 = new TicketTier { Id = Guid.NewGuid(), EventId = event1.Id, TierName = "GA", OriginalPrice = 100_000m };
        var tier2 = new TicketTier { Id = Guid.NewGuid(), EventId = event2.Id, TierName = "VIP", OriginalPrice = 200_000m };

        // 1. Resale Listings
        // listing1: Sold (by userB)
        var listing1 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event1.Id,
            TierId = tier1.Id,
            SellerId = userB.Id,
            OriginalTicketCode = "TCK-MS-01",
            OriginalPrice = 100_000m,
            ResalePrice = 100_000m,
            ListingStatus = ListingStatus.Sold,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        // listing2: Verified (by userC)
        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event1.Id,
            TierId = tier1.Id,
            SellerId = userC.Id,
            OriginalTicketCode = "TCK-MS-02",
            OriginalPrice = 100_000m,
            ResalePrice = 105_000m,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        // 2. Escrows
        // escrow1: Released (Buyer userA, Seller userB) -> Platform Revenue = 10k
        var escrow1 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing1.Id,
            BuyerId = userA.Id,
            SellerId = userB.Id,
            OriginalTicketPrice = 100_000m,
            BuyerFee = 5_000m,
            SellerFee = 5_000m,
            TotalBuyerPaid = 105_000m,
            NetSellerPayout = 95_000m,
            Status = EscrowStatus.Released,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        listing1.EscrowTransactions.Add(escrow1);

        // escrow2: Locked (Buyer userB, Seller userC) -> Pending fee = 15k
        var escrow2 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing2.Id,
            BuyerId = userB.Id,
            SellerId = userC.Id,
            OriginalTicketPrice = 150_000m,
            BuyerFee = 7_500m,
            SellerFee = 7_500m,
            TotalBuyerPaid = 157_500m,
            NetSellerPayout = 142_500m,
            Status = EscrowStatus.Locked,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        // escrow3: Refunded
        var escrow3 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing2.Id,
            BuyerId = userA.Id,
            SellerId = userC.Id,
            OriginalTicketPrice = 100_000m,
            BuyerFee = 5_000m,
            SellerFee = 5_000m,
            TotalBuyerPaid = 110_000m,
            NetSellerPayout = 95_000m,
            Status = EscrowStatus.Refunded,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3)
        };

        // 3. Tickets in Inventory (5 tickets across 5 statuses)
        var ticket1 = new Ticket { Id = Guid.NewGuid(), EventId = event1.Id, TierId = tier1.Id, CurrentOwnerId = userA.Id, TicketCode = "T-01", Status = TicketStatus.Valid };
        var ticket2 = new Ticket { Id = Guid.NewGuid(), EventId = event1.Id, TierId = tier1.Id, CurrentOwnerId = userB.Id, TicketCode = "T-02", Status = TicketStatus.LockedForResale };
        var ticket3 = new Ticket { Id = Guid.NewGuid(), EventId = event1.Id, TierId = tier1.Id, CurrentOwnerId = userA.Id, TicketCode = "T-03", Status = TicketStatus.Transferred };
        var ticket4 = new Ticket { Id = Guid.NewGuid(), EventId = event2.Id, TierId = tier2.Id, CurrentOwnerId = userB.Id, TicketCode = "T-04", Status = TicketStatus.Used };
        var ticket5 = new Ticket { Id = Guid.NewGuid(), EventId = event2.Id, TierId = tier2.Id, CurrentOwnerId = userC.Id, TicketCode = "T-05", Status = TicketStatus.Cancelled };

        db.Organizers.Add(org);
        db.ShadowUsers.AddRange(userA, userB, userC);
        db.Events.AddRange(event1, event2);
        db.TicketTiers.AddRange(tier1, tier2);
        db.ResaleListings.AddRange(listing1, listing2);
        db.EscrowTransactions.AddRange(escrow1, escrow2, escrow3);
        db.Tickets.AddRange(ticket1, ticket2, ticket3, ticket4, ticket5);

        await db.SaveChangesAsync();
    }
}

