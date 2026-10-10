using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.API.Middlewares;
using TicketShield.Application.Common.Behaviors;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Disputes;
using TicketShield.Application.Features.Disputes.Commands.ResolveDispute;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;

namespace TicketShield.UnitTests.Features.Disputes;

public class ResolveDisputeCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenAdminApproves_RecordsFullRefundAndWritesNoPayout()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db, recommendation: DisputeRecommendation.RecommendRejectFraud);
        var handler = new ResolveDisputeCommandHandler(db, new StubUser(seeded.AdminId, "Admin", true));

        var result = await handler.Handle(Command(seeded.DisputeId, DisputeDecision.Approve, "  Vé lỗi thật  "), CancellationToken.None);

        db.ChangeTracker.Clear();
        var dispute = await db.Disputes.AsNoTracking().SingleAsync();
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(DisputeStatus.Resolved, dispute.Status);
        Assert.Equal(DisputeResolution.RefundBuyer, dispute.Resolution);
        Assert.Equal(550_000m, dispute.RefundAmount);
        Assert.Equal("Vé lỗi thật", dispute.AdminNotes);
        Assert.Equal(seeded.AdminId, dispute.ResolvedBy);
        Assert.NotNull(dispute.ResolvedAt);
        Assert.Equal(EscrowStatus.Refunded, escrow.Status);
        Assert.Equal(nameof(DisputeStatus.Resolved), result.Data!.DisputeStatus);
        Assert.Equal(nameof(EscrowStatus.Refunded), result.Data.EscrowStatus);
        Assert.Empty(await db.OutboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Handle_WhenAdminRejectsAndSellerHasAccount_ReleasesNetPayout()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db, accountNumber: "0123456789");
        var handler = new ResolveDisputeCommandHandler(db, new StubUser(seeded.AdminId, "Admin", true));

        await handler.Handle(Command(seeded.DisputeId, DisputeDecision.Reject, "Đã vào cửa"), CancellationToken.None);

        db.ChangeTracker.Clear();
        var dispute = await db.Disputes.AsNoTracking().SingleAsync();
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync();
        var letter = Assert.Single(await db.OutboxMessages.AsNoTracking().ToListAsync());
        var payout = Assert.Single(await db.PayoutTransactions.AsNoTracking().ToListAsync());
        var payload = JsonSerializer.Deserialize<PayoutRequestedEvent>(letter.Payload);
        Assert.Equal(DisputeStatus.Rejected, dispute.Status);
        Assert.Equal(DisputeResolution.ReleaseSeller, dispute.Resolution);
        Assert.Equal(0, dispute.RefundAmount);
        Assert.Equal(EscrowStatus.Releasing, escrow.Status);
        Assert.Equal(nameof(PayoutRequestedEvent), letter.EventType);
        Assert.Equal(PayoutIdempotency.Key(seeded.EscrowId, 0), payload!.IdempotencyKey);
        Assert.Equal(475_000m, payload.Amount);
        Assert.Equal("0123456789", payload.AccountNumber);
        Assert.Equal(PayoutStatus.Processing, payout.Status);
        Assert.Equal(475_000m, payout.Amount);
    }

    [Fact]
    public async Task Handle_WhenSellerHasNoAccount_LeavesTheDisputeOpen()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db, accountNumber: "  ");
        var handler = new ResolveDisputeCommandHandler(db, new StubUser(seeded.AdminId, "Admin", true));

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(Command(seeded.DisputeId, DisputeDecision.Reject, "Bác bỏ"), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(422, await StatusCodeOf(ex));
        Assert.Equal(DisputeStatus.Open, (await db.Disputes.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(EscrowStatus.Disputed, (await db.EscrowTransactions.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.OutboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Handle_WhenDisputeIsAlreadyResolved_LeavesTheStoredDecision()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db, disputeStatus: DisputeStatus.Resolved, accountNumber: "0123456789");
        var handler = new ResolveDisputeCommandHandler(db, new StubUser(seeded.AdminId, "Admin", true));

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(Command(seeded.DisputeId, DisputeDecision.Approve, "Bấm lại"), CancellationToken.None));

        db.ChangeTracker.Clear();
        var dispute = await db.Disputes.AsNoTracking().SingleAsync();
        Assert.Equal(DisputeStatus.Resolved, dispute.Status);
        Assert.Null(dispute.AdminNotes);
        Assert.Empty(await db.OutboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Handle_WhenEscrowIsNotDisputed_WritesNoPayout()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db, escrowStatus: EscrowStatus.Locked, accountNumber: "0123456789");
        var handler = new ResolveDisputeCommandHandler(db, new StubUser(seeded.AdminId, "Admin", true));

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(Command(seeded.DisputeId, DisputeDecision.Reject, "Trả người bán"), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(EscrowStatus.Locked, (await db.EscrowTransactions.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(DisputeStatus.Open, (await db.Disputes.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.OutboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheBuyer_Returns403()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        var handler = new ResolveDisputeCommandHandler(db, new StubUser(seeded.BuyerId, "User", true));

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(Command(seeded.DisputeId, DisputeDecision.Approve, "Tự duyệt"), CancellationToken.None));

        Assert.Equal(403, await StatusCodeOf(ex));
    }

    [Fact]
    public async Task Handle_WhenReasonIsBlank_Returns400()
    {
        var behavior = new ValidationBehavior<ResolveDisputeCommand, ApiResponse<ResolveDisputeResponse>>(
            new IValidator<ResolveDisputeCommand>[] { new ResolveDisputeCommandValidator() });

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(
                Command(Guid.NewGuid(), DisputeDecision.Approve, "   "),
                () => throw new InvalidOperationException("handler ran"),
                CancellationToken.None));

        Assert.Equal(400, await StatusCodeOf(ex));
    }

    [Fact]
    public async Task Handle_WhenSavingTheDecisionFails_RestoresTheFrozenEscrow()
    {
        await using var db = CreateFailingDb();
        var seeded = await SeedAsync(db);
        var handler = new ResolveDisputeCommandHandler(db, new StubUser(seeded.AdminId, "Admin", true));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(Command(seeded.DisputeId, DisputeDecision.Approve, "Lưu lỗi"), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(DisputeStatus.Open, (await db.Disputes.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(EscrowStatus.Disputed, (await db.EscrowTransactions.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.OutboxMessages.AsNoTracking().ToListAsync());
    }

    private static ResolveDisputeCommand Command(Guid disputeId, DisputeDecision decision, string reason) =>
        new() { DisputeId = disputeId, Decision = decision, Reason = reason };

    private static TicketShieldDbContext CreateDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>().UseSqlite(connection).Options;
        var db = new TicketShieldDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static TicketShieldDbContext CreateFailingDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>().UseSqlite(connection).Options;
        var db = new ResolveSaveFailingContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<Seed> SeedAsync(
        TicketShieldDbContext db,
        string? recommendation = null,
        string accountNumber = "",
        DisputeStatus disputeStatus = DisputeStatus.Open,
        EscrowStatus escrowStatus = EscrowStatus.Disputed)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var admin = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = $"admin-{suffix}@test.local",
            FullName = "Admin",
            Role = UserRole.Admin
        };
        var buyer = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = $"buyer-{suffix}@test.local",
            FullName = "Buyer"
        };
        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = $"seller-{suffix}@test.local",
            FullName = "Seller",
            PayoutBankCode = "VCB",
            PayoutAccountNumber = accountNumber,
            PayoutAccountName = "SELLER"
        };
        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "Organizer",
            OfficialEmail = $"org-{suffix}@test.local"
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
            OriginalPrice = 500_000m
        };
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = concert.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = $"TCK-{suffix}",
            OriginalPrice = 500_000m,
            ResalePrice = 500_000m,
            ListingStatus = ListingStatus.Sold
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            Status = escrowStatus,
            TotalBuyerPaid = 550_000m,
            NetSellerPayout = 475_000m
        };
        var dispute = new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow.Id,
            BuyerId = buyer.Id,
            DisputeCode = "DP-RESOLVE",
            Status = disputeStatus,
            Description = "Vé lỗi",
            Recommendation = recommendation
        };
        db.Organizers.Add(organizer);
        db.Events.Add(concert);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(admin, buyer, seller);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new Seed(dispute.Id, escrow.Id, buyer.Id, admin.Id);
    }

    private static async Task<int> StatusCodeOf(Exception exception)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw exception,
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        return context.Response.StatusCode;
    }

    private sealed record Seed(Guid DisputeId, Guid EscrowId, Guid BuyerId, Guid AdminId);

    private sealed class ResolveSaveFailingContext : TicketShieldDbContext
    {
        public ResolveSaveFailingContext(DbContextOptions<TicketShieldDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<Dispute>().Any(entry => entry.State == EntityState.Modified))
            {
                throw new InvalidOperationException("resolve save failed");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class StubUser(Guid? userId, string? role, bool authenticated) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email => "staff@test.local";
        public string? Role { get; } = role;
        public bool IsAuthenticated { get; } = authenticated;
    }
}
