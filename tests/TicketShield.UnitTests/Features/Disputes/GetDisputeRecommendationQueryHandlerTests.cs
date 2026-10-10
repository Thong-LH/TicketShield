using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.API.Middlewares;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Disputes;
using TicketShield.Application.Features.Disputes.Queries.GetDisputeRecommendation;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;

namespace TicketShield.UnitTests.Features.Disputes;

public class GetDisputeRecommendationQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenCallerIsMissing_Returns401()
    {
        await using var db = CreateDb();
        var handler = new GetDisputeRecommendationQueryHandler(db, null);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new GetDisputeRecommendationQuery { DisputeId = Guid.NewGuid() }, CancellationToken.None));

        Assert.Equal(401, await StatusCodeOf(ex));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheBuyer_Returns403()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        var handler = new GetDisputeRecommendationQueryHandler(db, new StubUser(seeded.BuyerId, "User", true));

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new GetDisputeRecommendationQuery { DisputeId = seeded.DisputeId }, CancellationToken.None));

        Assert.Equal(403, await StatusCodeOf(ex));
    }

    [Fact]
    public async Task Handle_WhenDisputeIsMissing_Returns404()
    {
        await using var db = CreateDb();
        var handler = new GetDisputeRecommendationQueryHandler(db, new StubUser(Guid.NewGuid(), "Admin", true));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetDisputeRecommendationQuery { DisputeId = Guid.NewGuid() }, CancellationToken.None));

        Assert.Equal(404, await StatusCodeOf(ex));
    }

    [Fact]
    public async Task Handle_WhenAdminReadsACompletedDossier_ReturnsTheStoredScans()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        var handler = new GetDisputeRecommendationQueryHandler(db, new StubUser(Guid.NewGuid(), "Admin", true));

        var result = await handler.Handle(
            new GetDisputeRecommendationQuery { DisputeId = seeded.DisputeId },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(DisputeRecommendation.RecommendRejectFraud, result.Data!.Recommendation);
        Assert.Equal(seeded.TransferredAt, result.Data.TransferredAt);
        Assert.Equal(seeded.HarvestedAt, result.Data.HarvestedAt);
        var scan = Assert.Single(result.Data.Scans);
        Assert.Equal("NEW-1", scan.TicketCode);
        Assert.Equal("SUCCESS", scan.ScanResult);
        Assert.Equal("Cổng A", scan.GateName);
    }

    [Fact]
    public async Task Handle_WhenCskhReadsAnEmptyLabel_ReturnsNoRecommendation()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db, recommendation: null);
        var handler = new GetDisputeRecommendationQueryHandler(db, new StubUser(Guid.NewGuid(), "Cskh", true));

        var result = await handler.Handle(
            new GetDisputeRecommendationQuery { DisputeId = seeded.DisputeId },
            CancellationToken.None);

        Assert.Null(result.Data!.Recommendation);
        Assert.Empty(result.Data.Scans);
    }

    private static TicketShieldDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    private static async Task<Seed> SeedAsync(TicketShieldDbContext db, string? recommendation = DisputeRecommendation.RecommendRejectFraud)
    {
        var transferred = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var harvested = transferred.AddHours(2);
        var buyerId = Guid.NewGuid();
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            Status = EscrowStatus.Disputed,
            NewTicketCode = "NEW-1",
            TransferredAt = transferred
        };
        var scans = recommendation == null
            ? Array.Empty<GateScan>()
            : new[]
            {
                new GateScan
                {
                    TicketCode = "NEW-1",
                    ScannedAt = transferred.AddMinutes(5),
                    ScanResult = "SUCCESS",
                    GateName = "Cổng A",
                    ScannerDeviceId = "dev-a"
                }
            };
        var dispute = new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow.Id,
            BuyerId = buyerId,
            DisputeCode = "DP-READ",
            Status = DisputeStatus.Open,
            Recommendation = recommendation,
            HarvestedAt = harvested,
            GateLogSnapshot = JsonSerializer.Serialize(scans, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })
        };
        db.EscrowTransactions.Add(escrow);
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync();
        return new Seed(dispute.Id, buyerId, transferred, harvested);
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

    private sealed record Seed(Guid DisputeId, Guid BuyerId, DateTimeOffset TransferredAt, DateTimeOffset HarvestedAt);

    private sealed class StubUser(Guid? userId, string? role, bool authenticated) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email => "staff@test.local";
        public string? Role { get; } = role;
        public bool IsAuthenticated { get; } = authenticated;
    }
}
