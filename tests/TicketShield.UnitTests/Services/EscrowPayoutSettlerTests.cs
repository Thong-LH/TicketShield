using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;

namespace TicketShield.UnitTests.Services;

public class EscrowPayoutSettlerTests
{
    [Fact]
    public async Task TrySettle_WhenSellerHasNoAccount_LeavesTheEscrowLocked()
    {
        await using var db = CreateDb();
        var escrowId = await SeedLockedEscrowAsync(db, netPayout: 250_000m);
        var gateway = new CountingGateway();
        var settler = new EscrowPayoutSettler(db, new EscrowSettlementCas(db), gateway, TimeSpan.Zero);

        var settled = await settler.TrySettleAsync(escrowId);

        Assert.False(settled);
        Assert.Equal(0, gateway.Calls);
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(row => row.Id == escrowId);
        Assert.Equal(EscrowStatus.Locked, escrow.Status);
        Assert.False(await db.PayoutTransactions.AnyAsync());
        Assert.False(await db.OutboxMessages.AnyAsync());
    }

    [Fact]
    public async Task TrySettle_WhenAccountNumberIsInvalid_FailsWithoutCallingTheGateway()
    {
        await using var db = CreateDb();
        var escrowId = await SeedLockedEscrowAsync(db, netPayout: 250_000m, accountNumber: "12", bankCode: "MB");
        var gateway = new CountingGateway();
        var settler = new EscrowPayoutSettler(db, new EscrowSettlementCas(db), gateway, TimeSpan.Zero);

        var settled = await settler.TrySettleAsync(escrowId);

        Assert.False(settled);
        Assert.Equal(0, gateway.Calls);
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(row => row.Id == escrowId);
        var payout = await db.PayoutTransactions.AsNoTracking().SingleAsync(row => row.EscrowId == escrowId);
        Assert.Equal(EscrowStatus.Releasing, escrow.Status);
        Assert.Equal(PayoutStatus.Failed, payout.Status);
        Assert.Equal("STK không hợp lệ", payout.LastErrorMessage);
        Assert.Equal("12", payout.RecipientAccountNumber);
    }

    [Fact]
    public async Task TrySettle_WhenAccountIsValid_ReleasesItAndStoresTheGatewayReceipt()
    {
        await using var db = CreateDb();
        var escrowId = await SeedLockedEscrowAsync(db, netPayout: 250_000m, accountNumber: "0938434102", bankCode: "MB", accountName: "SELLER");
        var settler = new EscrowPayoutSettler(db, new EscrowSettlementCas(db), new MockNapasPayoutGateway(), TimeSpan.Zero);

        var settled = await settler.TrySettleAsync(escrowId);

        Assert.True(settled);
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(row => row.Id == escrowId);
        var payout = await db.PayoutTransactions.AsNoTracking().SingleAsync(row => row.EscrowId == escrowId);
        Assert.Equal(EscrowStatus.Released, escrow.Status);
        Assert.False(escrow.InSettlementBuffer);
        Assert.Equal(PayoutStatus.Success, payout.Status);
        Assert.Equal(250_000m, payout.Amount);
        Assert.Equal("MB", payout.RecipientBankCode);
        Assert.Equal("0938434102", payout.RecipientAccountNumber);
        Assert.Equal("SELLER", payout.RecipientAccountName);
        Assert.False(string.IsNullOrWhiteSpace(payout.BankReferenceCode));
        var outbox = await db.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Contains($"IDEMP-{escrowId}-0", outbox.Payload);
    }

    [Fact]
    public async Task TrySettle_WhenGatewayTimesOutTwiceThenSucceeds_StoresRetryCountTwo()
    {
        await using var db = CreateDb();
        var escrowId = await SeedLockedEscrowAsync(db, netPayout: 100_000m, accountNumber: "0938434102", bankCode: "MB");
        var gateway = new MockNapasPayoutGateway();
        gateway.TimeoutNext(escrowId, 2);
        var settler = new EscrowPayoutSettler(db, new EscrowSettlementCas(db), gateway, TimeSpan.Zero);

        var settled = await settler.TrySettleAsync(escrowId);

        Assert.True(settled);
        var payout = await db.PayoutTransactions.AsNoTracking().SingleAsync(row => row.EscrowId == escrowId);
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(row => row.Id == escrowId);
        Assert.Equal(EscrowStatus.Released, escrow.Status);
        Assert.Equal(PayoutStatus.Success, payout.Status);
        Assert.Equal(2, payout.RetryCount);
        Assert.False(string.IsNullOrWhiteSpace(payout.BankReferenceCode));
    }

    [Fact]
    public async Task TrySettle_WhenGatewayTimesOutThreeTimes_LeavesPayoutProcessing()
    {
        await using var db = CreateDb();
        var escrowId = await SeedLockedEscrowAsync(db, netPayout: 100_000m, accountNumber: "0938434102", bankCode: "MB");
        var gateway = new MockNapasPayoutGateway();
        gateway.TimeoutNext(escrowId, 3);
        var settler = new EscrowPayoutSettler(db, new EscrowSettlementCas(db), gateway, TimeSpan.Zero);

        var settled = await settler.TrySettleAsync(escrowId);

        Assert.False(settled);
        var payout = await db.PayoutTransactions.AsNoTracking().SingleAsync(row => row.EscrowId == escrowId);
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(row => row.Id == escrowId);
        Assert.Equal(EscrowStatus.Releasing, escrow.Status);
        Assert.Equal(PayoutStatus.Processing, payout.Status);
        Assert.Equal(3, payout.RetryCount);
        Assert.Equal("Ngân hàng timeout", payout.LastErrorMessage);
        Assert.True(string.IsNullOrWhiteSpace(payout.BankReferenceCode));
    }

    [Fact]
    public async Task TrySettle_WhenGatewayRejectsTheAccount_FailsWithoutRetry()
    {
        await using var db = CreateDb();
        var escrowId = await SeedLockedEscrowAsync(db, netPayout: 100_000m, accountNumber: "0938434102", bankCode: "MB");
        var gateway = new MockNapasPayoutGateway();
        gateway.RejectAccount(escrowId);
        var settler = new EscrowPayoutSettler(db, new EscrowSettlementCas(db), gateway, TimeSpan.Zero);

        var settled = await settler.TrySettleAsync(escrowId);

        Assert.False(settled);
        var payout = await db.PayoutTransactions.AsNoTracking().SingleAsync(row => row.EscrowId == escrowId);
        Assert.Equal(PayoutStatus.Failed, payout.Status);
        Assert.Equal(0, payout.RetryCount);
        Assert.Equal("STK không hợp lệ", payout.LastErrorMessage);
    }

    private static TicketShieldDbContext CreateDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new TicketShieldDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<Guid> SeedLockedEscrowAsync(
        TicketShieldDbContext db,
        decimal netPayout,
        string accountNumber = "",
        string bankCode = "",
        string accountName = "")
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var organizer = new Organizer { Id = Guid.NewGuid(), Name = "Org", OfficialEmail = $"org-{suffix}@test.local" };
        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = $"seller-{suffix}@test.local",
            FullName = "Seller",
            PayoutBankCode = bankCode,
            PayoutAccountNumber = accountNumber,
            PayoutAccountName = accountName
        };
        var buyer = new ShadowUser { Id = Guid.NewGuid(), Email = $"buyer-{suffix}@test.local", FullName = "Buyer" };
        var concert = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "Concert",
            Venue = "Hall",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(10),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(2),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(9)
        };
        var tier = new TicketTier { Id = Guid.NewGuid(), EventId = concert.Id, TierName = "GA", OriginalPrice = 100_000m };
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = concert.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = $"TCK-{suffix}",
            OriginalPrice = 100_000m,
            ResalePrice = 100_000m,
            ListingStatus = ListingStatus.Sold
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            Status = EscrowStatus.Locked,
            InSettlementBuffer = true,
            NetSellerPayout = netPayout
        };
        db.AddRange(organizer, seller, buyer, concert, tier, listing, escrow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return escrow.Id;
    }

    private sealed class CountingGateway : IPayoutGateway
    {
        public int Calls { get; private set; }

        public Task<PayoutGatewayResult> TransferAsync(PayoutTransferRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new PayoutGatewayResult
            {
                Outcome = PayoutGatewayOutcome.Succeeded,
                IdempotencyKey = PayoutIdempotency.Key(request.EscrowId, request.RetryCount),
                BankReferenceCode = "FTTEST",
                Amount = request.Amount
            });
        }
    }
}
