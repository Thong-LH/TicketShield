using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Features.Admin.Transactions.Queries.GetAdminTransactions;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features;

public class AdminTransactionsQueryTests
{
    [Fact]
    public async Task Handle_DefaultQuery_ReturnsPaginatedListAndAccurateSummaryMetrics()
    {
        await using var db = CreateDb();
        await SeedSampleDataAsync(db);

        var handler = new GetAdminTransactionsQueryHandler(db);
        var query = new GetAdminTransactionsQuery { Page = 1, Size = 10 };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(4, result.Data.Transactions.TotalCount);
        Assert.Equal(4, result.Data.Transactions.Items.Count);

        // Check Summary metrics (no filter = every escrow)
        // Inflow: buyer paid on escrow1 105_000 + escrow2 157_500 + escrow3 105_000 + escrow4 115_000
        Assert.Equal(482_500m, result.Data.Summary.TotalInflowAmount);

        // Held in escrow: buyer paid on the two Locked escrows 105_000 + 157_500
        Assert.Equal(262_500m, result.Data.Summary.TotalEscrowLockedAmount);
        Assert.Equal(2, result.Data.Summary.TotalLockedCount);

        // Paid out: the Success payout row of escrow3
        Assert.Equal(85_000m, result.Data.Summary.TotalPayoutCompletedAmount);

        // Refunded: TotalBuyerPaid 115_000
        Assert.Equal(115_000m, result.Data.Summary.TotalRefundedAmount);

        // In dispute: escrow2 has an open dispute row but its status is still Locked, so nothing is Disputed
        Assert.Equal(0, result.Data.Summary.TotalDisputedCount);

        // Net revenue: fees of the only Released escrow, escrow3 5_000 + 5_000
        Assert.Equal(10_000m, result.Data.Summary.TotalNetRevenue);
    }

    [Fact]
    public async Task Handle_FilterByDate_SummaryFollowsTheFilteredRows()
    {
        await using var db = CreateDb();
        await SeedSampleDataAsync(db);

        var handler = new GetAdminTransactionsQueryHandler(db);
        // Only escrow1 (-2h) and escrow2 (-5h) were created in the last day.
        var query = new GetAdminTransactionsQuery { FromDate = DateTimeOffset.UtcNow.AddDays(-1) };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Equal(2, result.Data!.Transactions.TotalCount);
        Assert.Equal(262_500m, result.Data.Summary.TotalInflowAmount);
        Assert.Equal(262_500m, result.Data.Summary.TotalEscrowLockedAmount);
        Assert.Equal(2, result.Data.Summary.TotalLockedCount);
        Assert.Equal(0m, result.Data.Summary.TotalPayoutCompletedAmount);
        Assert.Equal(0m, result.Data.Summary.TotalRefundedAmount);
        Assert.Equal(0m, result.Data.Summary.TotalNetRevenue);
    }

    [Fact]
    public async Task Handle_FilterByRefundedStatus_HeldAndPaidOutDropToZero()
    {
        await using var db = CreateDb();
        await SeedSampleDataAsync(db);

        var handler = new GetAdminTransactionsQueryHandler(db);
        var query = new GetAdminTransactionsQuery { Status = EscrowStatus.Refunded };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Equal(1, result.Data!.Transactions.TotalCount);
        Assert.Equal(0m, result.Data.Summary.TotalEscrowLockedAmount);
        Assert.Equal(0, result.Data.Summary.TotalLockedCount);
        Assert.Equal(0m, result.Data.Summary.TotalPayoutCompletedAmount);
        Assert.Equal(115_000m, result.Data.Summary.TotalRefundedAmount);
        Assert.Equal(115_000m, result.Data.Summary.TotalInflowAmount);
        Assert.Equal(0m, result.Data.Summary.TotalNetRevenue);
    }

    [Fact]
    public async Task Handle_SummaryCountsEveryPage_NotOnlyTheCurrentOne()
    {
        await using var db = CreateDb();
        await SeedSampleDataAsync(db);

        var handler = new GetAdminTransactionsQueryHandler(db);
        var query = new GetAdminTransactionsQuery { Page = 2, Size = 2 };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Equal(2, result.Data!.Transactions.Items.Count);
        Assert.Equal(482_500m, result.Data.Summary.TotalInflowAmount);
        Assert.Equal(2, result.Data.Summary.TotalLockedCount);
    }

    [Fact]
    public async Task Handle_MoneyBuckets_ReconcileToNetRevenue()
    {
        await using var db = CreateDb();
        await AdminMoneyTestData.SeedAsync(db, DateTimeOffset.UtcNow.AddHours(-3));

        var handler = new GetAdminTransactionsQueryHandler(db);
        var result = await handler.Handle(new GetAdminTransactionsQuery(), CancellationToken.None);
        var summary = result.Data!.Summary;

        Assert.Equal(AdminMoneyTestData.ExpectedInflow, summary.TotalInflowAmount);
        // RefundQueued and Disputed money is still on the platform, so it is part of Held.
        Assert.Equal(AdminMoneyTestData.ExpectedHeld, summary.TotalEscrowLockedAmount);
        Assert.Equal(AdminMoneyTestData.ExpectedHeldCount, summary.TotalLockedCount);
        Assert.Equal(AdminMoneyTestData.ExpectedPaidOut, summary.TotalPayoutCompletedAmount);
        Assert.Equal(AdminMoneyTestData.ExpectedRefunded, summary.TotalRefundedAmount);
        Assert.Equal(AdminMoneyTestData.ExpectedNetRevenue, summary.TotalNetRevenue);
        Assert.Equal(
            summary.TotalNetRevenue,
            summary.TotalInflowAmount - summary.TotalPayoutCompletedAmount - summary.TotalRefundedAmount - summary.TotalEscrowLockedAmount);
    }

    [Fact]
    public async Task Handle_ReleasedWithoutSuccessPayout_NetRevenueAddsOnlyItsFees()
    {
        await using var db = CreateDb();
        await AdminMoneyTestData.SeedAsync(db, DateTimeOffset.UtcNow.AddHours(-3));

        // Like the two dev seed escrows: Released, but no payout row went through.
        var anyListing = db.ResaleListings.First();
        db.EscrowTransactions.Add(new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = anyListing.Id,
            BuyerId = db.ShadowUsers.First().Id,
            SellerId = anyListing.SellerId,
            OriginalTicketPrice = 48_000m,
            BuyerFee = 2_000m,
            SellerFee = 2_000m,
            TotalBuyerPaid = 50_000m,
            NetSellerPayout = 46_000m,
            Status = EscrowStatus.Released,
            PaymentReference = "PAY-RELEASED-NO-PAYOUT",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        });
        await db.SaveChangesAsync();

        var handler = new GetAdminTransactionsQueryHandler(db);
        var summary = (await handler.Handle(new GetAdminTransactionsQuery(), CancellationToken.None)).Data!.Summary;

        Assert.Equal(AdminMoneyTestData.ExpectedNetRevenue + 4_000m, summary.TotalNetRevenue);
        // The subtraction would also count the 46_000 the seller never received.
        Assert.Equal(
            summary.TotalNetRevenue + 46_000m,
            summary.TotalInflowAmount - summary.TotalPayoutCompletedAmount - summary.TotalRefundedAmount - summary.TotalEscrowLockedAmount);
    }

    [Fact]
    public async Task Handle_InDispute_CountsOnlyEscrowsStillDisputed()
    {
        await using var db = CreateDb();
        await AdminMoneyTestData.SeedAsync(db, DateTimeOffset.UtcNow.AddHours(-3));

        var handler = new GetAdminTransactionsQueryHandler(db);
        var result = await handler.Handle(new GetAdminTransactionsQuery(), CancellationToken.None);

        // The Released escrow carries a rejected dispute row; only the Disputed escrow counts.
        Assert.Equal(AdminMoneyTestData.ExpectedInDispute, result.Data!.Summary.TotalDisputedCount);
    }

    [Fact]
    public async Task Handle_PayoutInFlight_IsHeldNotPaidOut()
    {
        await using var db = CreateDb();
        await SeedSampleDataAsync(db);

        // A Releasing escrow whose payout is still Processing.
        var anyListing = db.ResaleListings.First();
        var releasing = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = anyListing.Id,
            BuyerId = db.ShadowUsers.First().Id,
            SellerId = anyListing.SellerId,
            TotalBuyerPaid = 210_000m,
            NetSellerPayout = 190_000m,
            Status = EscrowStatus.Releasing,
            InSettlementBuffer = true,
            PaymentReference = "PAY-RELEASING-001",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        db.EscrowTransactions.Add(releasing);
        db.PayoutTransactions.Add(new PayoutTransaction
        {
            Id = Guid.NewGuid(),
            EscrowId = releasing.Id,
            SellerId = anyListing.SellerId,
            PayoutCode = "PO-RELEASING-001",
            Amount = 190_000m,
            Status = PayoutStatus.Processing,
            RecipientBankCode = "VCB",
            RecipientAccountNumber = "1234567890",
            RecipientAccountName = "SELLER",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var handler = new GetAdminTransactionsQueryHandler(db);
        var result = await handler.Handle(new GetAdminTransactionsQuery(), CancellationToken.None);

        Assert.Equal(262_500m + 210_000m, result.Data!.Summary.TotalEscrowLockedAmount);
        Assert.Equal(3, result.Data.Summary.TotalLockedCount);
        Assert.Equal(85_000m, result.Data.Summary.TotalPayoutCompletedAmount);
    }

    [Fact]
    public async Task Handle_FilterByStatus_ReturnsOnlyMatchingStatus()
    {
        await using var db = CreateDb();
        await SeedSampleDataAsync(db);

        var handler = new GetAdminTransactionsQueryHandler(db);
        var query = new GetAdminTransactionsQuery { Status = EscrowStatus.Locked };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.All(result.Data!.Transactions.Items, item => Assert.Equal(EscrowStatus.Locked, item.Status));
        Assert.Equal(2, result.Data.Transactions.TotalCount);
    }

    [Fact]
    public async Task Handle_FilterByPayoutStatus_ReturnsOnlyMatchingPayoutStatus()
    {
        await using var db = CreateDb();
        await SeedSampleDataAsync(db);

        var handler = new GetAdminTransactionsQueryHandler(db);
        var query = new GetAdminTransactionsQuery { PayoutStatus = PayoutStatus.Success };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.Data!.Transactions.Items);
        Assert.Equal(PayoutStatus.Success, result.Data.Transactions.Items[0].PayoutStatus);
    }

    [Fact]
    public async Task Handle_FilterByInSettlementBufferOnly_ReturnsOnlyBuffered()
    {
        await using var db = CreateDb();
        await SeedSampleDataAsync(db);

        var handler = new GetAdminTransactionsQueryHandler(db);
        var query = new GetAdminTransactionsQuery { InSettlementBufferOnly = true };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.All(result.Data!.Transactions.Items, item => Assert.True(item.InSettlementBuffer));
    }

    [Fact]
    public async Task Handle_SearchKeyword_ReturnsMatchingRecords()
    {
        await using var db = CreateDb();
        await SeedSampleDataAsync(db);

        var handler = new GetAdminTransactionsQueryHandler(db);

        // Search by Event Name (event1 has 2 escrows: escrow1 and escrow2)
        var queryEvent = new GetAdminTransactionsQuery { Keyword = "BlackPink" };
        var resultEvent = await handler.Handle(queryEvent, CancellationToken.None);
        Assert.Equal(2, resultEvent.Data!.Transactions.Items.Count);
        Assert.All(resultEvent.Data.Transactions.Items, item => Assert.Contains("BlackPink", item.EventName));

        // Search by Ticket Code
        var queryCode = new GetAdminTransactionsQuery { Keyword = "TCK-BP-01" };
        var resultCode = await handler.Handle(queryCode, CancellationToken.None);
        Assert.Single(resultCode.Data!.Transactions.Items);
        Assert.Equal("TCK-BP-01", resultCode.Data.Transactions.Items[0].OriginalTicketCode);

        // Search by Buyer Email (buyer1 bought escrow1 and escrow3)
        var queryBuyer = new GetAdminTransactionsQuery { Keyword = "buyer.anh@test.local" };
        var resultBuyer = await handler.Handle(queryBuyer, CancellationToken.None);
        Assert.Equal(2, resultBuyer.Data!.Transactions.Items.Count);
        Assert.All(resultBuyer.Data.Transactions.Items, item => Assert.Equal("buyer.anh@test.local", item.BuyerEmail));
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPageAndMetadata()
    {
        await using var db = CreateDb();
        await SeedSampleDataAsync(db);

        var handler = new GetAdminTransactionsQueryHandler(db);
        var query = new GetAdminTransactionsQuery { Page = 2, Size = 2 };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Transactions.PageNumber);
        Assert.Equal(2, result.Data.Transactions.PageSize);
        Assert.Equal(4, result.Data.Transactions.TotalCount);
        Assert.Equal(2, result.Data.Transactions.TotalPages);
        Assert.Equal(2, result.Data.Transactions.Items.Count);
        Assert.True(result.Data.Transactions.HasPreviousPage);
        Assert.False(result.Data.Transactions.HasNextPage);
    }

    [Fact]
    public async Task Handle_FilterByDateBoundary_ReturnsExactMatchesWithinVietnamLocalDay()
    {
        await using var db = CreateDb();

        var org = new Organizer { Id = Guid.NewGuid(), Name = "Org", OfficialEmail = "org@test.local" };
        var buyer = new ShadowUser { Id = Guid.NewGuid(), FullName = "Buyer", Email = "buyer@test.local" };
        var seller = new ShadowUser { Id = Guid.NewGuid(), FullName = "Seller", Email = "seller@test.local" };
        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "Concert",
            Venue = "Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(7),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(6)
        };
        var tier = new TicketTier { Id = Guid.NewGuid(), EventId = ev.Id, TierName = "Standard", OriginalPrice = 100_000m };
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            TicketCode = "TCK-BOUNDARY-01",
            EventId = ev.Id,
            TierId = tier.Id,
            CurrentOwnerId = seller.Id,
            OriginalPrice = 100_000m,
            Status = TicketStatus.LockedForResale
        };
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-BOUNDARY-01",
            ResalePrice = 100_000m,
            ListingStatus = ListingStatus.Verified
        };

        db.Organizers.Add(org);
        db.ShadowUsers.AddRange(buyer, seller);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.Tickets.Add(ticket);
        db.ResaleListings.Add(listing);

        // 3 mốc thời gian:
        // Tx1: 17/11 00:30 (+07) -> UTC: 16/11 17:30
        var tx1 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            Status = EscrowStatus.Released,
            TotalBuyerPaid = 105_000m,
            NetSellerPayout = 95_000m,
            CreatedAt = new DateTimeOffset(2026, 11, 17, 0, 30, 0, TimeSpan.FromHours(7))
        };

        // Tx2: 17/11 23:30 (+07) -> UTC: 17/11 16:30
        var tx2 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            Status = EscrowStatus.Released,
            TotalBuyerPaid = 105_000m,
            NetSellerPayout = 95_000m,
            CreatedAt = new DateTimeOffset(2026, 11, 17, 23, 30, 0, TimeSpan.FromHours(7))
        };

        // Tx3: 18/11 00:30 (+07) -> UTC: 17/11 17:30
        var tx3 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            Status = EscrowStatus.Released,
            TotalBuyerPaid = 105_000m,
            NetSellerPayout = 95_000m,
            CreatedAt = new DateTimeOffset(2026, 11, 18, 0, 30, 0, TimeSpan.FromHours(7))
        };

        db.EscrowTransactions.AddRange(tx1, tx2, tx3);
        await db.SaveChangesAsync();

        var handler = new GetAdminTransactionsQueryHandler(db);

        // Filter 17/11 -> 17/11 theo UTC mà FE gửi:
        // 17/11 00:00 (+07) = 16/11 17:00 UTC
        // 17/11 23:59:59.999 (+07) = 17/11 16:59:59.999 UTC
        var fromUtc = DateTimeOffset.Parse("2026-11-16T17:00:00.000Z");
        var toUtc = DateTimeOffset.Parse("2026-11-17T16:59:59.999Z");

        var query = new GetAdminTransactionsQuery
        {
            FromDate = fromUtc,
            ToDate = toUtc
        };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Transactions.TotalCount);
        Assert.Equal(2, result.Data.Transactions.Items.Count);
        Assert.Contains(result.Data.Transactions.Items, item => item.EscrowId == tx1.Id);
        Assert.Contains(result.Data.Transactions.Items, item => item.EscrowId == tx2.Id);
        Assert.DoesNotContain(result.Data.Transactions.Items, item => item.EscrowId == tx3.Id);
    }

    private static TicketShieldDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    private static async Task SeedSampleDataAsync(TicketShieldDbContext db)
    {
        var org = new Organizer { Id = Guid.NewGuid(), Name = "LiveNation", OfficialEmail = "contact@livenation.vn" };
        var buyer1 = new ShadowUser { Id = Guid.NewGuid(), FullName = "Nguyen Van Anh", Email = "buyer.anh@test.local", PhoneNumber = "0901111111" };
        var buyer2 = new ShadowUser { Id = Guid.NewGuid(), FullName = "Tran Thi Binh", Email = "buyer.binh@test.local", PhoneNumber = "0902222222" };
        var seller1 = new ShadowUser { Id = Guid.NewGuid(), FullName = "Le Van Cuong", Email = "seller.cuong@test.local", PhoneNumber = "0903333333" };
        var seller2 = new ShadowUser { Id = Guid.NewGuid(), FullName = "Pham Thi Dung", Email = "seller.dung@test.local", PhoneNumber = "0904444444" };

        var event1 = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "BlackPink World Tour",
            Venue = "My Dinh National Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(7),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(6)
        };

        var event2 = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "Anh Trai Say Hi",
            Venue = "Saigon Exhibition Center",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(14),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(14).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(13)
        };

        var tier1 = new TicketTier { Id = Guid.NewGuid(), EventId = event1.Id, TierName = "VIP", OriginalPrice = 100_000m };
        var tier2 = new TicketTier { Id = Guid.NewGuid(), EventId = event2.Id, TierName = "Standard", OriginalPrice = 150_000m };

        var listing1 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event1.Id,
            TierId = tier1.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-BP-01",
            OriginalPrice = 100_000m,
            ResalePrice = 100_000m,
            ListingStatus = ListingStatus.Sold
        };

        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event1.Id,
            TierId = tier1.Id,
            SellerId = seller2.Id,
            OriginalTicketCode = "TCK-BP-02",
            OriginalPrice = 100_000m,
            ResalePrice = 100_000m,
            ListingStatus = ListingStatus.Sold
        };

        var listing3 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event2.Id,
            TierId = tier2.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-ATSH-01",
            OriginalPrice = 150_000m,
            ResalePrice = 150_000m,
            ListingStatus = ListingStatus.Sold
        };

        var listing4 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = event2.Id,
            TierId = tier2.Id,
            SellerId = seller2.Id,
            OriginalTicketCode = "TCK-ATSH-02",
            OriginalPrice = 150_000m,
            ResalePrice = 150_000m,
            ListingStatus = ListingStatus.Sold
        };

        // Escrow 1: Locked & In Settlement Buffer
        var escrow1 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing1.Id,
            BuyerId = buyer1.Id,
            SellerId = seller1.Id,
            OriginalTicketPrice = 100_000m,
            BuyerFee = 5_000m,
            SellerFee = 5_000m,
            TotalBuyerPaid = 105_000m,
            NetSellerPayout = 80_000m,
            Status = EscrowStatus.Locked,
            InSettlementBuffer = true,
            UnlockAt = DateTimeOffset.UtcNow.AddHours(12),
            TransferredAt = DateTimeOffset.UtcNow.AddHours(-1),
            PaymentReference = "PAY-BP-001",
            NewTicketCode = "NEW-BP-001",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };

        // Escrow 2: Locked (Disputed)
        var escrow2 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing2.Id,
            BuyerId = buyer2.Id,
            SellerId = seller2.Id,
            OriginalTicketPrice = 150_000m,
            BuyerFee = 7_500m,
            SellerFee = 7_500m,
            TotalBuyerPaid = 157_500m,
            NetSellerPayout = 120_000m,
            Status = EscrowStatus.Locked,
            InSettlementBuffer = false,
            UnlockAt = DateTimeOffset.UtcNow.AddDays(1),
            PaymentReference = "PAY-BP-002",
            NewTicketCode = "NEW-BP-002",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-5)
        };
        var dispute2 = new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow2.Id,
            BuyerId = buyer2.Id,
            DisputeCode = "DSP-BP-001",
            Status = DisputeStatus.Open,
            Description = "Ticket Invalid"
        };
        escrow2.Dispute = dispute2;

        // Escrow 3: Released with Payout Completed
        var escrow3 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing3.Id,
            BuyerId = buyer1.Id,
            SellerId = seller1.Id,
            OriginalTicketPrice = 100_000m,
            BuyerFee = 5_000m,
            SellerFee = 5_000m,
            TotalBuyerPaid = 105_000m,
            NetSellerPayout = 85_000m,
            Status = EscrowStatus.Released,
            InSettlementBuffer = false,
            PaymentReference = "PAY-ATSH-001",
            NewTicketCode = "NEW-ATSH-001",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        var payout3 = new PayoutTransaction
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow3.Id,
            SellerId = seller1.Id,
            PayoutCode = "PO-ATSH-001",
            Amount = 85_000m,
            Status = PayoutStatus.Success,
            RecipientBankCode = "VCB",
            RecipientAccountNumber = "1234567890",
            RecipientAccountName = "LE VAN CUONG",
            BankReferenceCode = "BANK-REF-999",
            ProcessedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        escrow3.PayoutTransaction = payout3;

        // Escrow 4: Refunded
        var escrow4 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing4.Id,
            BuyerId = buyer2.Id,
            SellerId = seller2.Id,
            OriginalTicketPrice = 110_000m,
            BuyerFee = 5_000m,
            SellerFee = 5_000m,
            TotalBuyerPaid = 115_000m,
            NetSellerPayout = 90_000m,
            Status = EscrowStatus.Refunded,
            InSettlementBuffer = false,
            PaymentReference = "PAY-ATSH-002",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        db.Organizers.Add(org);
        db.ShadowUsers.AddRange(buyer1, buyer2, seller1, seller2);
        db.Events.AddRange(event1, event2);
        db.TicketTiers.AddRange(tier1, tier2);
        db.ResaleListings.AddRange(listing1, listing2, listing3, listing4);
        db.EscrowTransactions.AddRange(escrow1, escrow2, escrow3, escrow4);
        db.PayoutTransactions.Add(payout3);
        db.Disputes.Add(dispute2);

        await db.SaveChangesAsync();
    }
}
