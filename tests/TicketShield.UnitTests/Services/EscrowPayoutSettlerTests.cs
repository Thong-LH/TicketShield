using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;

namespace TicketShield.UnitTests.Services;

public class EscrowPayoutSettlerTests
{
    [Fact]
    public async Task TrySettle_WhenEscrowIsLocked_ReleasesItAndStoresTheGatewayReceipt()
    {
        await using var db = CreateDb();
        var escrowId = await SeedLockedEscrowAsync(db, netPayout: 250_000m);
        var settler = new EscrowPayoutSettler(db, new EscrowSettlementCas(db), new MockNapasPayoutGateway());

        var settled = await settler.TrySettleAsync(escrowId);

        Assert.True(settled);
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(row => row.Id == escrowId);
        var payout = await db.PayoutTransactions.AsNoTracking().SingleAsync(row => row.EscrowId == escrowId);
        Assert.Equal(EscrowStatus.Released, escrow.Status);
        Assert.False(escrow.InSettlementBuffer);
        Assert.Equal(PayoutStatus.Success, payout.Status);
        Assert.Equal(250_000m, payout.Amount);
        Assert.False(string.IsNullOrWhiteSpace(payout.BankReferenceCode));
        Assert.Equal("", payout.RecipientBankCode);
        Assert.Equal("", payout.RecipientAccountNumber);
        var outbox = await db.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Contains($"IDEMP-{escrowId}-0", outbox.Payload);
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

    private static async Task<Guid> SeedLockedEscrowAsync(TicketShieldDbContext db, decimal netPayout)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var organizer = new Organizer { Id = Guid.NewGuid(), Name = "Org", OfficialEmail = $"org-{suffix}@test.local" };
        var seller = new ShadowUser { Id = Guid.NewGuid(), Email = $"seller-{suffix}@test.local", FullName = "Seller" };
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
}
