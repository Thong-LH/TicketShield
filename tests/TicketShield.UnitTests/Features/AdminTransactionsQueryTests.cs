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

        // Check Summary metrics
        // Locked + Buffer: NetSellerPayout 80_000 + 120_000 = 200_000
        Assert.Equal(200_000m, result.Data.Summary.TotalEscrowLockedAmount);
        Assert.Equal(2, result.Data.Summary.TotalLockedCount);

        // Released / Payout completed: NetSellerPayout 85_000
        Assert.Equal(85_000m, result.Data.Summary.TotalPayoutCompletedAmount);

        // Refunded: TotalBuyerPaid 115_000
        Assert.Equal(115_000m, result.Data.Summary.TotalRefundedAmount);

        // Disputed: 1
        Assert.Equal(1, result.Data.Summary.TotalDisputedCount);
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
