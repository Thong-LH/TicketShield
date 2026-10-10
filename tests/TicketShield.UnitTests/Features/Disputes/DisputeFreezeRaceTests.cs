using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Disputes.Commands.CreateDispute;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;

namespace TicketShield.UnitTests.Features.Disputes;

public class DisputeFreezeRaceTests
{
    [Fact]
    public async Task Handle_WhenDisputeCommitsFirst_ReleaseLosesAndStoresNoPayout()
    {
        var connectionString = NewConnectionString();
        try
        {
            var seeded = await SeedAsync(connectionString);
            await using var disputeDb = Open(connectionString);
            await using var releaseDb = Open(connectionString);

            await new CreateDisputeCommandHandler(disputeDb, new StubUser(seeded.BuyerId)).Handle(
                new CreateDisputeCommand { EscrowId = seeded.EscrowId, Reason = "Vé sai khán đài" },
                CancellationToken.None);
            var releaseWon = await new EscrowSettlementCas(releaseDb).TryBeginReleaseAsync(seeded.EscrowId);

            var state = await ReadAsync(connectionString, seeded.EscrowId);
            Assert.False(releaseWon);
            Assert.Equal(EscrowStatus.Disputed, state.Status);
            Assert.Equal(1, state.DisputeCount);
            Assert.Equal(0, state.PayoutCount);
            Assert.True(state.InSettlementBuffer);
        }
        finally
        {
            DeleteDatabase(connectionString);
        }
    }

    [Fact]
    public async Task TryBeginRelease_WhenReleaseCommitsFirst_DisputeThrowsAndStoresNoDispute()
    {
        var connectionString = NewConnectionString();
        try
        {
            var seeded = await SeedAsync(connectionString);
            await using var releaseDb = Open(connectionString);
            await using var disputeDb = Open(connectionString);

            var releaseWon = await new EscrowSettlementCas(releaseDb).TryBeginReleaseAsync(seeded.EscrowId);
            var ex = await Assert.ThrowsAsync<DisputeDeadlineExpiredException>(() =>
                new CreateDisputeCommandHandler(disputeDb, new StubUser(seeded.BuyerId)).Handle(
                    new CreateDisputeCommand { EscrowId = seeded.EscrowId, Reason = "Vé sai khán đài" },
                    CancellationToken.None));

            var state = await ReadAsync(connectionString, seeded.EscrowId);
            Assert.True(releaseWon);
            Assert.IsType<DisputeDeadlineExpiredException>(ex);
            Assert.Equal(EscrowStatus.Releasing, state.Status);
            Assert.Equal(0, state.DisputeCount);
            Assert.Equal(1, state.PayoutCount);
            Assert.Equal("PayoutRequestedEvent", state.PayoutEventType);
        }
        finally
        {
            DeleteDatabase(connectionString);
        }
    }

    [Fact]
    public async Task Handle_WhenDisputeAndReleaseRunTogether_ExactlyOneSideWins()
    {
        for (var round = 0; round < 20; round++)
        {
            var connectionString = NewConnectionString();
            try
            {
                var seeded = await SeedAsync(connectionString);
                await using var disputeDb = Open(connectionString);
                await using var releaseDb = Open(connectionString);
                var handler = new CreateDisputeCommandHandler(disputeDb, new StubUser(seeded.BuyerId));
                var cas = new EscrowSettlementCas(releaseDb);
                var disputeTask = handler.Handle(
                    new CreateDisputeCommand { EscrowId = seeded.EscrowId, Reason = "Vé sai khán đài" },
                    CancellationToken.None);
                var releaseTask = cas.TryBeginReleaseAsync(seeded.EscrowId);

                try
                {
                    await Task.WhenAll(disputeTask, releaseTask);
                }
                catch (Exception ex) when (LostDispute(ex, disputeTask))
                {
                    if (releaseTask.IsFaulted)
                    {
                        throw releaseTask.Exception!.GetBaseException();
                    }
                }

                var state = await ReadAsync(connectionString, seeded.EscrowId);
                var disputeWon = disputeTask.IsCompletedSuccessfully;
                var releaseWon = releaseTask.IsCompletedSuccessfully && await releaseTask;
                if (disputeWon)
                {
                    Assert.False(releaseWon);
                    Assert.Equal(EscrowStatus.Disputed, state.Status);
                    Assert.Equal(1, state.DisputeCount);
                    Assert.Equal(0, state.PayoutCount);
                    Assert.True(state.InSettlementBuffer);
                }
                else
                {
                    Assert.True(releaseWon);
                    Assert.Equal(EscrowStatus.Releasing, state.Status);
                    Assert.Equal(0, state.DisputeCount);
                    Assert.Equal(1, state.PayoutCount);
                    Assert.Equal("PayoutRequestedEvent", state.PayoutEventType);
                }
            }
            finally
            {
                DeleteDatabase(connectionString);
            }
        }
    }

    private static bool LostDispute(Exception ex, Task disputeTask)
    {
        if (ex is DisputeDeadlineExpiredException)
        {
            return true;
        }

        return disputeTask.Exception?.GetBaseException() is DisputeDeadlineExpiredException;
    }

    private static string NewConnectionString()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-dispute-race-{Guid.NewGuid():N}.db");
        return new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30,
            Pooling = false
        }.ConnectionString;
    }

    private static TicketShieldDbContext Open(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new TicketShieldDbContext(options);
    }

    private static async Task<(Guid EscrowId, Guid BuyerId)> SeedAsync(string connectionString)
    {
        await using var db = Open(connectionString);
        await db.Database.EnsureCreatedAsync();

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
            FullName = "Seller",
            PayoutBankCode = "MB",
            PayoutAccountNumber = "0938434102",
            PayoutAccountName = "SELLER"
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
            Status = EscrowStatus.Locked,
            InSettlementBuffer = true,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            NetSellerPayout = 100_000m
        };

        db.Organizers.Add(organizer);
        db.ShadowUsers.AddRange(seller, buyer);
        db.Events.Add(concert);
        db.TicketTiers.Add(tier);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();
        return (escrow.Id, buyer.Id);
    }

    private static async Task<RaceRow> ReadAsync(string connectionString, Guid escrowId)
    {
        await using var db = Open(connectionString);
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(row => row.Id == escrowId);
        var disputeCount = await db.Disputes.AsNoTracking().CountAsync(row => row.EscrowId == escrowId);
        var payouts = await db.OutboxMessages.AsNoTracking().ToListAsync();
        return new RaceRow(escrow.Status, escrow.InSettlementBuffer, disputeCount, payouts.Count, payouts.SingleOrDefault()?.EventType);
    }

    private static void DeleteDatabase(string connectionString)
    {
        var path = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record RaceRow(
        EscrowStatus Status,
        bool InSettlementBuffer,
        int DisputeCount,
        int PayoutCount,
        string? PayoutEventType);

    private sealed class StubUser(Guid? userId) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email => "buyer@test.local";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;
    }
}
