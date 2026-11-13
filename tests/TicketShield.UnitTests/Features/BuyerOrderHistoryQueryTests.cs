using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Orders.Queries.GetBuyerOrderHistory;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features;

public class BuyerOrderHistoryQueryTests
{
    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ThrowsUnauthorized()
    {
        await using var db = CreateDb();
        var handler = new GetBuyerOrderHistoryQueryHandler(db, new MockCurrentUserService(null));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new GetBuyerOrderHistoryQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ReturnsOnlyCurrentBuyerOrdersWithAccurateFinancialFields()
    {
        await using var db = CreateDb();
        var buyerId = Guid.NewGuid();
        var otherBuyerId = Guid.NewGuid();

        await SeedSampleOrdersAsync(db, buyerId, otherBuyerId);

        var handler = new GetBuyerOrderHistoryQueryHandler(db, new MockCurrentUserService(buyerId));
        var query = new GetBuyerOrderHistoryQuery { Page = 1, Size = 10 };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        // Buyer has 3 orders (2 Released/Locked, 1 Refunded), otherBuyer has 1 order
        Assert.Equal(3, result.Data.Orders.TotalCount);
        Assert.Equal(3, result.Data.Orders.Items.Count);

        // Summary
        Assert.Equal(3, result.Data.Summary.TotalOrders);
        Assert.Equal(220_000m, result.Data.Summary.TotalSpent); // 105k + 115k = 220k
        Assert.Equal(105_000m, result.Data.Summary.TotalRefunded); // 105k
        Assert.Equal(1, result.Data.Summary.InEscrowOrdersCount); // 1 Locked

        // First item checks
        var first = result.Data.Orders.Items[0];
        Assert.NotNull(first.PaymentReference);
        Assert.True(first.TotalBuyerPaid > 0);
        Assert.NotEmpty(first.EventName);
        Assert.NotEmpty(first.OrderCode);
    }

    [Fact]
    public async Task Handle_RefundStatusAndAmounts_MappedCorrectly()
    {
        await using var db = CreateDb();
        var buyerId = Guid.NewGuid();
        var otherBuyerId = Guid.NewGuid();

        await SeedSampleOrdersAsync(db, buyerId, otherBuyerId);

        var handler = new GetBuyerOrderHistoryQueryHandler(db, new MockCurrentUserService(buyerId));
        var query = new GetBuyerOrderHistoryQuery { Status = EscrowStatus.Refunded };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        var order = Assert.Single(result.Data!.Orders.Items);
        Assert.True(order.IsRefunded);
        Assert.Equal(105_000m, order.RefundAmount);
        Assert.NotNull(order.RefundedAt);
        Assert.Contains("Vé không hợp lệ", order.RefundReason);
        Assert.Equal("Đã hoàn tiền cho người mua", order.StatusDescription);
    }

    [Fact]
    public async Task Handle_FilterByStatus_ReturnsMatchingOrders()
    {
        await using var db = CreateDb();
        var buyerId = Guid.NewGuid();
        var otherBuyerId = Guid.NewGuid();

        await SeedSampleOrdersAsync(db, buyerId, otherBuyerId);

        var handler = new GetBuyerOrderHistoryQueryHandler(db, new MockCurrentUserService(buyerId));
        var query = new GetBuyerOrderHistoryQuery { Status = EscrowStatus.Locked };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        var order = Assert.Single(result.Data!.Orders.Items);
        Assert.Equal(EscrowStatus.Locked, order.Status);
        Assert.Equal("Đã thanh toán - Đang giữ ký quỹ an toàn", order.StatusDescription);
    }

    [Fact]
    public async Task Handle_KeywordSearch_MatchesEventOrPaymentRef()
    {
        await using var db = CreateDb();
        var buyerId = Guid.NewGuid();
        var otherBuyerId = Guid.NewGuid();

        await SeedSampleOrdersAsync(db, buyerId, otherBuyerId);

        var handler = new GetBuyerOrderHistoryQueryHandler(db, new MockCurrentUserService(buyerId));

        // Search by Event Name
        var queryEvent = new GetBuyerOrderHistoryQuery { Keyword = "Coldplay" };
        var resultEvent = await handler.Handle(queryEvent, CancellationToken.None);
        Assert.True(resultEvent.Success);
        Assert.Single(resultEvent.Data!.Orders.Items);
        Assert.Contains("Coldplay", resultEvent.Data.Orders.Items[0].EventName);

        // Search by PaymentReference
        var queryPay = new GetBuyerOrderHistoryQuery { Keyword = "PAY-REF-001" };
        var resultPay = await handler.Handle(queryPay, CancellationToken.None);
        Assert.True(resultPay.Success);
        Assert.Single(resultPay.Data!.Orders.Items);
        Assert.Equal("PAY-REF-001", resultPay.Data.Orders.Items[0].PaymentReference);
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPageAndMetadata()
    {
        await using var db = CreateDb();
        var buyerId = Guid.NewGuid();
        var otherBuyerId = Guid.NewGuid();

        await SeedSampleOrdersAsync(db, buyerId, otherBuyerId);

        var handler = new GetBuyerOrderHistoryQueryHandler(db, new MockCurrentUserService(buyerId));
        var query = new GetBuyerOrderHistoryQuery { Page = 2, Size = 2 };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Orders.PageNumber);
        Assert.Equal(2, result.Data.Orders.PageSize);
        Assert.Equal(3, result.Data.Orders.TotalCount);
        Assert.Equal(2, result.Data.Orders.TotalPages);
        Assert.Single(result.Data.Orders.Items);
        Assert.True(result.Data.Orders.HasPreviousPage);
        Assert.False(result.Data.Orders.HasNextPage);
    }

    private static TicketShieldDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    private static async Task SeedSampleOrdersAsync(TicketShieldDbContext db, Guid buyerId, Guid otherBuyerId)
    {
        var org = new Organizer { Id = Guid.NewGuid(), Name = "LiveNation Asia", OfficialEmail = "contact@livenation.asia" };

        var buyer = new ShadowUser { Id = buyerId, FullName = "Nguyen Van Buyer", Email = "buyer@test.local", PhoneNumber = "0901234567" };
        var otherBuyer = new ShadowUser { Id = otherBuyerId, FullName = "Other Buyer", Email = "other@test.local", PhoneNumber = "0909999999" };
        var seller = new ShadowUser { Id = Guid.NewGuid(), FullName = "Le Van Seller", Email = "seller@test.local", PhoneNumber = "0908888888" };

        var event1 = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "Coldplay Music of the Spheres",
            Venue = "National Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(25),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(25).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(24)
        };

        var event2 = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "Imagine Dragons Tour",
            Venue = "Exhibition Center",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(50),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(50).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(49)
        };

        var tier1 = new TicketTier { Id = Guid.NewGuid(), EventId = event1.Id, TierName = "CAT 1", OriginalPrice = 100_000m };
        var tier2 = new TicketTier { Id = Guid.NewGuid(), EventId = event2.Id, TierName = "VIP", OriginalPrice = 200_000m };

        var listing1 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event1.Id,
            TierId = tier1.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-CP-01",
            OriginalPrice = 100_000m,
            ResalePrice = 100_000m,
            ListingStatus = ListingStatus.Sold,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event2.Id,
            TierId = tier2.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ID-01",
            OriginalPrice = 110_000m,
            ResalePrice = 110_000m,
            ListingStatus = ListingStatus.Sold,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };

        var listing3 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event2.Id,
            TierId = tier2.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ID-02",
            OriginalPrice = 100_000m,
            ResalePrice = 100_000m,
            ListingStatus = ListingStatus.Sold,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        // Order 1: Released (Coldplay) for buyerId
        var order1 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing1.Id,
            BuyerId = buyerId,
            SellerId = seller.Id,
            OriginalTicketPrice = 100_000m,
            BuyerFee = 5_000m,
            SellerFee = 5_000m,
            TotalBuyerPaid = 105_000m,
            NetSellerPayout = 95_000m,
            PaymentReference = "PAY-REF-001",
            BankTransactionReference = "SEPAY-TXN-001",
            NewTicketCode = "NEW-TICK-CP-01",
            Status = EscrowStatus.Released,
            TransferredAt = DateTimeOffset.UtcNow.AddDays(-9),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        // Order 2: Locked in Escrow (Imagine Dragons) for buyerId
        var order2 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing2.Id,
            BuyerId = buyerId,
            SellerId = seller.Id,
            OriginalTicketPrice = 110_000m,
            BuyerFee = 5_000m,
            SellerFee = 5_000m,
            TotalBuyerPaid = 115_000m,
            NetSellerPayout = 105_000m,
            PaymentReference = "PAY-REF-002",
            BankTransactionReference = "SEPAY-TXN-002",
            NewTicketCode = "NEW-TICK-ID-01",
            Status = EscrowStatus.Locked,
            InSettlementBuffer = true,
            UnlockAt = DateTimeOffset.UtcNow.AddHours(12),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };

        // Order 3: Refunded (with Dispute) for buyerId
        var order3 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing3.Id,
            BuyerId = buyerId,
            SellerId = seller.Id,
            OriginalTicketPrice = 100_000m,
            BuyerFee = 5_000m,
            SellerFee = 5_000m,
            TotalBuyerPaid = 105_000m,
            NetSellerPayout = 95_000m,
            PaymentReference = "PAY-REF-003",
            BankTransactionReference = "SEPAY-TXN-003",
            Status = EscrowStatus.Refunded,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        var dispute3 = new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = order3.Id,
            BuyerId = buyerId,
            DisputeCode = "DSP-REF-003",
            ReasonCode = DisputeReasonCode.TicketInvalid,
            Description = "Vé không hợp lệ tại cổng",
            Status = DisputeStatus.Resolved,
            Resolution = DisputeResolution.RefundBuyer,
            RefundAmount = 105_000m
        };
        order3.Dispute = dispute3;

        // Order 4: For otherBuyerId (Must be isolated and not returned for buyerId)
        var order4 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing1.Id,
            BuyerId = otherBuyerId,
            SellerId = seller.Id,
            OriginalTicketPrice = 100_000m,
            BuyerFee = 5_000m,
            TotalBuyerPaid = 105_000m,
            PaymentReference = "PAY-OTHER-001",
            Status = EscrowStatus.Released,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        db.Organizers.Add(org);
        db.ShadowUsers.AddRange(buyer, otherBuyer, seller);
        db.Events.AddRange(event1, event2);
        db.TicketTiers.AddRange(tier1, tier2);
        db.ResaleListings.AddRange(listing1, listing2, listing3);
        db.EscrowTransactions.AddRange(order1, order2, order3, order4);
        db.Disputes.Add(dispute3);

        await db.SaveChangesAsync();
    }

    private sealed class MockCurrentUserService : ICurrentUserService
    {
        public MockCurrentUserService(Guid? userId) => UserId = userId;

        public Guid? UserId { get; }
        public string? Email => "buyer@test.local";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;
    }
}

