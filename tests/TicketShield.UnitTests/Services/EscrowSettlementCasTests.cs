using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;

namespace TicketShield.UnitTests.Services;

public class EscrowSettlementCasTests
{
    [Fact]
    public async Task TryBeginRelease_WhenEscrowIsLocked_MovesItToReleasing()
    {
        await using var db = CreateDb();
        var escrowId = await SeedEscrowAsync(db, EscrowStatus.Locked);
        var cas = new EscrowSettlementCas(db);

        var won = await cas.TryBeginReleaseAsync(escrowId);

        Assert.True(won);
        Assert.Equal(EscrowStatus.Releasing, await CurrentStatusAsync(db, escrowId));
    }

    [Fact]
    public async Task TryBeginRelease_WhenDisputeAlreadyWon_LeavesStatusDisputed()
    {
        await using var db = CreateDb();
        var escrowId = await SeedEscrowAsync(db, EscrowStatus.Locked);
        var cas = new EscrowSettlementCas(db);

        var disputeWon = await cas.TryBeginDisputeAsync(escrowId);
        var releaseWon = await cas.TryBeginReleaseAsync(escrowId);

        Assert.True(disputeWon);
        Assert.False(releaseWon);
        Assert.Equal(EscrowStatus.Disputed, await CurrentStatusAsync(db, escrowId));
    }

    [Fact]
    public async Task TryBeginDispute_WhenReleaseAlreadyWon_LeavesStatusReleasing()
    {
        await using var db = CreateDb();
        var escrowId = await SeedEscrowAsync(db, EscrowStatus.Locked);
        var cas = new EscrowSettlementCas(db);

        var releaseWon = await cas.TryBeginReleaseAsync(escrowId);
        var disputeWon = await cas.TryBeginDisputeAsync(escrowId);

        Assert.True(releaseWon);
        Assert.False(disputeWon);
        Assert.Equal(EscrowStatus.Releasing, await CurrentStatusAsync(db, escrowId));
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

    private static async Task<Guid> SeedEscrowAsync(TicketShieldDbContext db, EscrowStatus status)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "Organizer",
            OfficialEmail = $"org-{suffix}@test.local"
        };
        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = $"seller-{suffix}@test.local",
            FullName = "Seller"
        };
        var buyer = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = $"buyer-{suffix}@test.local",
            FullName = "Buyer"
        };
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
        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = concert.Id,
            TierName = "GA",
            OriginalPrice = 100_000m
        };
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
            Status = status,
            InSettlementBuffer = true
        };

        db.Organizers.Add(organizer);
        db.ShadowUsers.AddRange(seller, buyer);
        db.Events.Add(concert);
        db.TicketTiers.Add(tier);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return escrow.Id;
    }

    private static async Task<EscrowStatus> CurrentStatusAsync(TicketShieldDbContext db, Guid escrowId)
    {
        db.ChangeTracker.Clear();
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(e => e.Id == escrowId);
        return escrow.Status;
    }
}
