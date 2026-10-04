using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using TicketShield.Infrastructure.Workers;

namespace TicketShield.UnitTests.Workers;

public class StuckSettlementWorkerTests
{
    private sealed class FakeSettlement : ISettlementClient
    {
        public List<string> SentKeys { get; } = [];

        public Task<bool> SendAsync(PayoutRequestedEvent command, CancellationToken cancellationToken = default)
        {
            SentKeys.Add(command.IdempotencyKey);
            return Task.FromResult(true);
        }

        public Task<SettlementTransferStatus?> GetStatusAsync(string idempotencyKey, CancellationToken cancellationToken = default)
            => Task.FromResult<SettlementTransferStatus?>(null);
    }

    [Fact]
    public async Task Reconcile_ResumesOnlyReleasingEscrowsOlderThan15MinutesThatCanStillBePaid()
    {
        var databaseName = Guid.NewGuid().ToString();
        var settlement = new FakeSettlement();
        var services = new ServiceCollection();
        services.AddDbContext<TicketShieldDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<ITicketShieldDbContext>(provider => provider.GetRequiredService<TicketShieldDbContext>());
        services.AddScoped<IPayoutReportApplier, PayoutReportApplier>();
        services.AddSingleton<ISettlementClient>(settlement);
        services.AddSingleton<PayoutOutboxDispatcher>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var crashedId = Guid.NewGuid();
        var recentId = Guid.NewGuid();
        var failedId = Guid.NewGuid();
        var exhaustedId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        await using (var db = provider.GetRequiredService<TicketShieldDbContext>())
        {
            db.EscrowTransactions.AddRange(
                Escrow(crashedId, sellerId, DateTimeOffset.UtcNow.AddMinutes(-16)),
                Escrow(recentId, sellerId, DateTimeOffset.UtcNow.AddMinutes(-5)),
                Escrow(failedId, sellerId, DateTimeOffset.UtcNow.AddMinutes(-20)),
                Escrow(exhaustedId, sellerId, DateTimeOffset.UtcNow.AddMinutes(-20)));
            db.OutboxMessages.Add(Letter(crashedId, processed: false));
            db.OutboxMessages.Add(Letter(failedId, processed: true));
            db.OutboxMessages.Add(Letter(exhaustedId, processed: true));
            db.PayoutTransactions.AddRange(
                new PayoutTransaction
                {
                    Id = Guid.NewGuid(),
                    EscrowId = failedId,
                    SellerId = sellerId,
                    PayoutCode = "PO-FAILED",
                    Status = PayoutStatus.Failed
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
        Assert.Equal([$"IDEMP-{crashedId}-0"], settlement.SentKeys);
    }

    private static EscrowTransaction Escrow(Guid id, Guid sellerId, DateTimeOffset updatedAt) => new()
    {
        Id = id,
        ListingId = Guid.NewGuid(),
        BuyerId = Guid.NewGuid(),
        SellerId = sellerId,
        Status = EscrowStatus.Releasing,
        InSettlementBuffer = true,
        UpdatedAt = updatedAt
    };

    private static OutboxMessage Letter(Guid escrowId, bool processed)
    {
        var command = new PayoutRequestedEvent
        {
            EscrowId = escrowId,
            IdempotencyKey = $"IDEMP-{escrowId}-0"
        };
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = nameof(PayoutRequestedEvent),
            Payload = JsonSerializer.Serialize(command),
            ProcessedAt = processed ? DateTimeOffset.UtcNow : null
        };
    }
}
