using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.API.Middlewares;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Disputes;
using TicketShield.Application.Features.Disputes.Queries.GetMyDisputeProgress;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;

namespace TicketShield.UnitTests.Features.Disputes;

public class GetMyDisputeProgressQueryHandlerTests
{
    private const string StaffNote = "Bác bỏ khiếu nại và giải ngân Escrow cho Người bán.";

    [Fact]
    public async Task Handle_WhenCallerIsMissing_Returns401()
    {
        await using var db = CreateDb();
        var handler = new GetMyDisputeProgressQueryHandler(db, null);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new GetMyDisputeProgressQuery { DisputeId = Guid.NewGuid() }, CancellationToken.None));

        Assert.Equal(401, await StatusCodeOf(ex));
    }

    [Fact]
    public async Task Handle_WhenDisputeIsMissing_Returns404()
    {
        await using var db = CreateDb();
        var handler = new GetMyDisputeProgressQueryHandler(db, new StubUser(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetMyDisputeProgressQuery { DisputeId = Guid.NewGuid() }, CancellationToken.None));

        Assert.Equal(404, await StatusCodeOf(ex));
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_Returns403()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        var handler = new GetMyDisputeProgressQueryHandler(db, new StubUser(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new GetMyDisputeProgressQuery { DisputeId = seeded.DisputeId }, CancellationToken.None));

        Assert.Equal(403, await StatusCodeOf(ex));
    }

    [Fact]
    public async Task Handle_WhenCallerIsOnlyTheTicketRecipient_Returns403()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        var handler = new GetMyDisputeProgressQueryHandler(db, new StubUser(Guid.NewGuid(), seeded.RecipientEmail));

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new GetMyDisputeProgressQuery { DisputeId = seeded.DisputeId }, CancellationToken.None));

        Assert.Equal(403, await StatusCodeOf(ex));
    }

    [Fact]
    public async Task Handle_WhenBuyerReads_ReturnsBuyerView()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        var handler = new GetMyDisputeProgressQueryHandler(db, new StubUser(seeded.BuyerId));

        var result = await handler.Handle(new GetMyDisputeProgressQuery { DisputeId = seeded.DisputeId }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("BUYER", result.Data!.ViewerRole);
        Assert.Equal(seeded.DisputeId, result.Data.DisputeId);
        Assert.Equal(MyDisputeProgressMapper.BuyerRejectedMessage, result.Data.ResolutionMessage);
        Assert.Null(result.Data.Money);
    }

    [Fact]
    public async Task Handle_WhenSellerReads_ReturnsSellerViewWithPayout()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        var handler = new GetMyDisputeProgressQueryHandler(db, new StubUser(seeded.SellerId));

        var result = await handler.Handle(new GetMyDisputeProgressQuery { DisputeId = seeded.DisputeId }, CancellationToken.None);

        Assert.Equal("SELLER", result.Data!.ViewerRole);
        Assert.Null(result.Data.Description);
        Assert.Equal("PAYOUT", result.Data.Money!.Kind);
        Assert.Equal("SUCCESS", result.Data.Money.Status);
        Assert.Equal(seeded.PayoutProcessedAt, result.Data.Money.ProcessedAt);
    }

    [Fact]
    public async Task Handle_NeverLeaksStaffOrInternalData()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        var handler = new GetMyDisputeProgressQueryHandler(db, new StubUser(seeded.BuyerId));

        var result = await handler.Handle(new GetMyDisputeProgressQuery { DisputeId = seeded.DisputeId }, CancellationToken.None);
        // Không escape tiếng Việt, nếu không thì DoesNotContain luôn qua dù có lộ.
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        Assert.Contains("DP-MINE", json);
        Assert.Contains("Khiếu nại chưa đủ căn cứ", json);
        Assert.DoesNotContain(StaffNote, json);
        Assert.DoesNotContain("NEW-1", json);
        Assert.DoesNotContain(DisputeRecommendation.RecommendRejectFraud, json);
        Assert.DoesNotContain("Cổng A", json);
        Assert.DoesNotContain(seeded.StaffId.ToString(), json);
    }

    private static TicketShieldDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    private static async Task<Seed> SeedAsync(TicketShieldDbContext db)
    {
        var created = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var processed = created.AddHours(5);
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        const string recipientEmail = "friend@test.local";
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            NetSellerPayout = 1_450_000m,
            RecipientEmail = recipientEmail,
            Status = EscrowStatus.Released
        };
        var payout = new PayoutTransaction
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow.Id,
            SellerId = sellerId,
            Amount = 1_450_000m,
            Status = PayoutStatus.Success,
            ProcessedAt = processed
        };
        var dispute = new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow.Id,
            BuyerId = buyerId,
            DisputeCode = "DP-MINE",
            Description = "Vé không vào được cổng.",
            Status = DisputeStatus.Rejected,
            Resolution = DisputeResolution.ReleaseSeller,
            AdminNotes = StaffNote,
            Recommendation = DisputeRecommendation.RecommendRejectFraud,
            GateLogSnapshot = "[{\"ticketCode\":\"NEW-1\",\"gateName\":\"Cổng A\"}]",
            HarvestedAt = created.AddMinutes(10),
            ResolvedBy = staffId,
            ResolvedAt = created.AddHours(3),
            CreatedAt = created
        };
        db.EscrowTransactions.Add(escrow);
        db.PayoutTransactions.Add(payout);
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync();
        return new Seed(dispute.Id, buyerId, sellerId, staffId, recipientEmail, processed);
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

    private sealed record Seed(
        Guid DisputeId,
        Guid BuyerId,
        Guid SellerId,
        Guid StaffId,
        string RecipientEmail,
        DateTimeOffset PayoutProcessedAt);

    private sealed class StubUser(Guid userId, string email = "user@test.local") : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email { get; } = email;
        public string? Role => "User";
        public bool IsAuthenticated => true;
    }
}
