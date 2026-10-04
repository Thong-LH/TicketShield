using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Workers;

namespace TicketShield.UnitTests.Workers;

public class StuckSettlementWorkerTests
{
    private sealed class RecordingSettler : IEscrowPayoutSettler
    {
        public List<Guid> ResumedIds { get; } = [];

        public Task<bool> TrySettleAsync(Guid escrowId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> TryResumeReleaseAsync(Guid escrowId, CancellationToken cancellationToken = default)
        {
            ResumedIds.Add(escrowId);
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Reconcile_ResumesOnlyReleasingEscrowsOlderThan15MinutesThatCanStillBePaid()
    {
        var databaseName = Guid.NewGuid().ToString();
        var settler = new RecordingSettler();
        var services = new ServiceCollection();
        services.AddDbContext<TicketShieldDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<ITicketShieldDbContext>(provider => provider.GetRequiredService<TicketShieldDbContext>());
        services.AddSingleton<IEscrowPayoutSettler>(settler);
        var provider = services.BuildServiceProvider();

        var crashedId = Guid.NewGuid();
        var recentId = Guid.NewGuid();
        var failedId = Guid.NewGuid();
        var exhaustedId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        await using (var db = provider.GetRequiredService<TicketShieldDbContext>())
        {
            db.EscrowTransactions.AddRange(
                Escrow(crashedId, sellerId, EscrowStatus.Releasing, DateTimeOffset.UtcNow.AddMinutes(-16)),
                Escrow(recentId, sellerId, EscrowStatus.Releasing, DateTimeOffset.UtcNow.AddMinutes(-5)),
                Escrow(failedId, sellerId, EscrowStatus.Releasing, DateTimeOffset.UtcNow.AddMinutes(-20)),
                Escrow(exhaustedId, sellerId, EscrowStatus.Releasing, DateTimeOffset.UtcNow.AddMinutes(-20)));
            db.PayoutTransactions.AddRange(
                new PayoutTransaction
                {
                    Id = Guid.NewGuid(),
                    EscrowId = failedId,
                    SellerId = sellerId,
                    PayoutCode = "PO-FAILED",
                    Status = PayoutStatus.Failed,
                    RetryCount = 0
                },
                new PayoutTransaction
                {
                    Id = Guid.NewGuid(),
                    EscrowId = exhaustedId,
                    SellerId = sellerId,
                    PayoutCode = "PO-TIMEOUT",
                    Status = PayoutStatus.Processing,
                    RetryCount = 3
                });
            await db.SaveChangesAsync();
        }

        var worker = new StuckSettlementWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StuckSettlementWorker>.Instance,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMinutes(15));

        var recovered = await worker.ReconcileStuckReleasesAsync();

        Assert.Equal(1, recovered);
        Assert.Equal([crashedId], settler.ResumedIds);
    }

    private static EscrowTransaction Escrow(Guid id, Guid sellerId, EscrowStatus status, DateTimeOffset updatedAt) => new()
    {
        Id = id,
        ListingId = Guid.NewGuid(),
        BuyerId = Guid.NewGuid(),
        SellerId = sellerId,
        Status = status,
        InSettlementBuffer = true,
        UpdatedAt = updatedAt
    };
}
