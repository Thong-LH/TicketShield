using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.API.Middlewares;
using TicketShield.Application.Common.Behaviors;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Disputes.Commands.CreateDispute;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;

namespace TicketShield.UnitTests.Features.Disputes;

public class CreateDisputeCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenEscrowIsLocked_StoresOneOpenDisputeAndLeavesTheHoldFlag()
    {
        await using var db = CreateDb();
        var seeded = await SeedEscrowAsync(db, EscrowStatus.Locked, inSettlementBuffer: true);
        var handler = new CreateDisputeCommandHandler(db, new StubUser(seeded.BuyerId));

        var result = await handler.Handle(new CreateDisputeCommand
        {
            EscrowId = seeded.EscrowId,
            Reason = "  Vé sai khán đài  "
        }, CancellationToken.None);

        db.ChangeTracker.Clear();
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync();
        var dispute = await db.Disputes.AsNoTracking().SingleAsync();

        Assert.Equal(EscrowStatus.Disputed, escrow.Status);
        Assert.True(escrow.InSettlementBuffer);
        Assert.Equal(DisputeStatus.Open, dispute.Status);
        Assert.Equal(seeded.EscrowId, dispute.EscrowId);
        Assert.Equal(seeded.BuyerId, dispute.BuyerId);
        Assert.Equal("Vé sai khán đài", dispute.Description);
        Assert.Equal(DisputeReasonCode.TicketInvalid, dispute.ReasonCode);
        Assert.Equal(dispute.CreatedAt, result.Data!.CreatedAt);
        Assert.Equal($"DP-{dispute.Id.ToString("N")[..8].ToUpperInvariant()}", dispute.DisputeCode);
        Assert.Equal(dispute.Id, result.Data.DisputeId);
        var outbox = await db.OutboxMessages.AsNoTracking().ToListAsync();
        var harvest = Assert.Single(outbox);
        Assert.Equal(nameof(DisputeHarvestRequested), harvest.EventType);
        Assert.DoesNotContain(outbox, row => row.EventType == nameof(PayoutRequestedEvent));
        Assert.Equal(dispute.Id, System.Text.Json.JsonSerializer.Deserialize<DisputeHarvestRequested>(harvest.Payload)!.DisputeId);
    }

    [Theory]
    [InlineData(EscrowStatus.Releasing)]
    [InlineData(EscrowStatus.Released)]
    public async Task Handle_WhenPayoutAlreadyStarted_Returns422AndStoresNoDispute(EscrowStatus status)
    {
        await using var db = CreateDb();
        var seeded = await SeedEscrowAsync(db, status, inSettlementBuffer: true);
        var handler = new CreateDisputeCommandHandler(db, new StubUser(seeded.BuyerId));

        var ex = await Assert.ThrowsAsync<DisputeDeadlineExpiredException>(() => handler.Handle(new CreateDisputeCommand
        {
            EscrowId = seeded.EscrowId,
            Reason = "Bị từ chối ở cổng"
        }, CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(422, await StatusCodeOf(ex));
        Assert.Equal(status, (await db.EscrowTransactions.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.Disputes.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Handle_WhenDisputeAlreadyExists_Returns422AndDoesNotStoreASecondRow()
    {
        await using var db = CreateDb();
        var seeded = await SeedEscrowAsync(db, EscrowStatus.Disputed, inSettlementBuffer: true);
        db.Disputes.Add(new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = seeded.EscrowId,
            BuyerId = seeded.BuyerId,
            DisputeCode = "DP-EXIST01",
            ReasonCode = DisputeReasonCode.TicketInvalid,
            Description = "Đã gửi",
            Status = DisputeStatus.Open
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var handler = new CreateDisputeCommandHandler(db, new StubUser(seeded.BuyerId));

        var ex = await Assert.ThrowsAsync<DisputeDeadlineExpiredException>(() => handler.Handle(new CreateDisputeCommand
        {
            EscrowId = seeded.EscrowId,
            Reason = "Gửi lại"
        }, CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(422, await StatusCodeOf(ex));
        var disputes = await db.Disputes.AsNoTracking().ToListAsync();
        Assert.Single(disputes);
        Assert.Equal("DP-EXIST01", disputes[0].DisputeCode);
    }

    [Fact]
    public async Task Handle_WhenCallerIsMissing_Returns401()
    {
        await using var db = CreateDb();
        var seeded = await SeedEscrowAsync(db, EscrowStatus.Locked, inSettlementBuffer: true);
        var handler = new CreateDisputeCommandHandler(db, currentUser: null);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(new CreateDisputeCommand
        {
            EscrowId = seeded.EscrowId,
            Reason = "Vé sai"
        }, CancellationToken.None));

        Assert.Equal(401, await StatusCodeOf(ex));
        Assert.Empty(await db.Disputes.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Handle_WhenEscrowIsMissing_Returns404()
    {
        await using var db = CreateDb();
        var handler = new CreateDisputeCommandHandler(db, new StubUser(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new CreateDisputeCommand
        {
            EscrowId = Guid.NewGuid(),
            Reason = "Vé sai"
        }, CancellationToken.None));

        Assert.Equal(404, await StatusCodeOf(ex));
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotTheBuyer_Returns403AndLeavesStatus()
    {
        await using var db = CreateDb();
        var seeded = await SeedEscrowAsync(db, EscrowStatus.Locked, inSettlementBuffer: true);
        var handler = new CreateDisputeCommandHandler(db, new StubUser(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.Handle(new CreateDisputeCommand
        {
            EscrowId = seeded.EscrowId,
            Reason = "Vé sai"
        }, CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(403, await StatusCodeOf(ex));
        Assert.Equal(EscrowStatus.Locked, (await db.EscrowTransactions.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Handle_WhenReasonIsBlank_Returns400AndDoesNotRunTheHandler()
    {
        var behavior = new ValidationBehavior<CreateDisputeCommand, ApiResponse<CreateDisputeResponse>>(
            new IValidator<CreateDisputeCommand>[] { new CreateDisputeCommandValidator() });
        var command = new CreateDisputeCommand { EscrowId = Guid.NewGuid(), Reason = "   " };

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(command, () => throw new InvalidOperationException("handler ran"), CancellationToken.None));

        Assert.Equal(400, await StatusCodeOf(ex));
    }

    [Fact]
    public async Task Handle_WhenSavingTheDisputeFails_RollsTheEscrowBackToLocked()
    {
        await using var db = CreateFailingDb();
        var seeded = await SeedEscrowAsync(db, EscrowStatus.Locked, inSettlementBuffer: false);
        var handler = new CreateDisputeCommandHandler(db, new StubUser(seeded.BuyerId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new CreateDisputeCommand
        {
            EscrowId = seeded.EscrowId,
            Reason = "Vé không thấy trên app"
        }, CancellationToken.None));

        db.ChangeTracker.Clear();
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(EscrowStatus.Locked, escrow.Status);
        Assert.False(escrow.InSettlementBuffer);
        Assert.Empty(await db.Disputes.AsNoTracking().ToListAsync());
    }

    private static async Task<int> StatusCodeOf(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw exception,
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        return context.Response.StatusCode;
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

    private static TicketShieldDbContext CreateFailingDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new DisputeInsertFailingContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<(Guid EscrowId, Guid BuyerId)> SeedEscrowAsync(
        TicketShieldDbContext db,
        EscrowStatus status,
        bool inSettlementBuffer)
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
            InSettlementBuffer = inSettlementBuffer
        };

        db.Organizers.Add(organizer);
        db.ShadowUsers.AddRange(seller, buyer);
        db.Events.Add(concert);
        db.TicketTiers.Add(tier);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (escrow.Id, buyer.Id);
    }

    private sealed class DisputeInsertFailingContext : TicketShieldDbContext
    {
        public DisputeInsertFailingContext(DbContextOptions<TicketShieldDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<Dispute>().Any(entry => entry.State == EntityState.Added))
            {
                throw new InvalidOperationException("dispute save failed");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class StubUser(Guid? userId) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email => "buyer@test.local";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;
    }
}
