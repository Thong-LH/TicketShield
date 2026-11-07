using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Disputes;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using TicketShield.Infrastructure.Workers;
using Xunit;

namespace TicketShield.Resale.Tests;

public sealed class WorkerDatabaseFixture : IAsyncLifetime
{
    public static readonly Guid Seller = Guid.Parse(ResaleFixture.Seller);
    public static readonly Guid Buyer = Guid.Parse(ResaleFixture.OtherSeller);
    public static readonly Guid Event = Guid.Parse(ResaleFixture.Event);
    public static readonly Guid Tier = Guid.Parse(ResaleFixture.Tier);

    public PostgresCluster Database { get; } = new();

    // Production runs with Npgsql pooling on, so the workers must see a pooled connection too.
    public string PooledConnection { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await Database.Start();
        PooledConnection = new NpgsqlConnectionStringBuilder(Database.CoreConnection) { Pooling = true }.ConnectionString;

        await using var db = NewContext();
        await DatabaseSeeder.SeedTicketShieldAsync(db);
        var seller = await db.ShadowUsers.SingleAsync(user => user.Id == Seller);
        seller.PayoutBankCode = "VCB";
        seller.PayoutAccountNumber = "0123456789";
        seller.PayoutAccountName = "NGUYEN VAN SELLER";
        await db.SaveChangesAsync();
    }

    public TicketShieldDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TicketShieldDbContext>().UseNpgsql(PooledConnection).Options);

    public async Task DisposeAsync() => await Database.DisposeAsync();
}

/// <summary>
/// Background workers take a leader lock before each cycle. Their writes must reach PostgreSQL,
/// and the settlement CAS must be able to open its own transaction while the lock is held.
/// InMemory tests cannot see this, so these run on a real cluster.
/// </summary>
public sealed class WorkerLockOnPostgresTests(WorkerDatabaseFixture f) : IClassFixture<WorkerDatabaseFixture>
{
    [Fact]
    public async Task Dispute_harvest_saves_the_gate_evidence()
    {
        var escrow = await AddEscrowAsync(EscrowStatus.Disputed);
        var (disputeId, messageId) = await AddHarvestRequestAsync(escrow);
        await using var provider = Services(new FakeGate(), new FakeSettlement());

        var finished = await provider.GetRequiredService<DisputeHarvestWorker>().HarvestPendingAsync();

        await using var db = f.NewContext();
        var dispute = await db.Disputes.AsNoTracking().SingleAsync(row => row.Id == disputeId);
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == messageId);
        Assert.True(finished >= 1);
        Assert.NotNull(dispute.HarvestedAt);
        Assert.NotNull(message.ProcessedAt);
    }

    [Fact]
    public async Task Automatic_settlement_starts_the_payout_of_a_due_escrow()
    {
        var escrow = await AddEscrowAsync(EscrowStatus.Locked, row =>
        {
            row.InSettlementBuffer = true;
            row.UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        });
        await using var provider = Services(new FakeGate(), new FakeSettlement());

        var settled = await provider.GetRequiredService<AutomaticSettlementWorker>().ProcessAutomaticPayoutsAsync();

        await using var db = f.NewContext();
        var stored = await db.EscrowTransactions.AsNoTracking().SingleAsync(row => row.Id == escrow.Id);
        var escrowText = escrow.Id.ToString();
        Assert.True(settled >= 1);
        Assert.Equal(EscrowStatus.Releasing, stored.Status);
        Assert.True(await db.PayoutTransactions.AnyAsync(row => row.EscrowId == escrow.Id));
        Assert.True(await db.OutboxMessages.AnyAsync(row =>
            row.EventType == nameof(PayoutRequestedEvent) && row.Payload.Contains(escrowText)));
    }

    [Fact]
    public async Task Payout_dispatcher_remembers_a_message_it_has_sent()
    {
        var escrowId = Guid.NewGuid();
        var messageId = await AddPayoutRequestAsync(escrowId, processedAt: null);
        var settlement = new FakeSettlement();
        await using var provider = Services(new FakeGate(), settlement);

        await provider.GetRequiredService<PayoutOutboxDispatcher>().DispatchPendingAsync();

        await using var db = f.NewContext();
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == messageId);
        Assert.Contains(settlement.Sent, command => command.EscrowId == escrowId);
        Assert.NotNull(message.ProcessedAt);
    }

    [Fact]
    public async Task Stuck_settlement_saves_the_result_reported_by_settlement()
    {
        var escrow = await AddEscrowAsync(EscrowStatus.Releasing, row => row.UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1));
        await using (var seed = f.NewContext())
        {
            seed.PayoutTransactions.Add(new PayoutTransaction
            {
                EscrowId = escrow.Id,
                SellerId = escrow.SellerId,
                PayoutCode = "PO-" + Guid.NewGuid().ToString("N")[..10],
                Amount = escrow.NetSellerPayout
            });
            await seed.SaveChangesAsync();
        }
        await AddPayoutRequestAsync(escrow.Id, processedAt: DateTimeOffset.UtcNow.AddHours(-1));
        var settlement = new FakeSettlement { Status = new SettlementTransferStatus { State = "Succeeded", BankReference = "BANK-REF-1" } };
        await using var provider = Services(new FakeGate(), settlement);

        await provider.GetRequiredService<StuckSettlementWorker>().ReconcileStuckReleasesAsync();

        await using var db = f.NewContext();
        var stored = await db.EscrowTransactions.AsNoTracking().SingleAsync(row => row.Id == escrow.Id);
        var payout = await db.PayoutTransactions.AsNoTracking().SingleAsync(row => row.EscrowId == escrow.Id);
        Assert.Equal(EscrowStatus.Released, stored.Status);
        Assert.Equal(PayoutStatus.Success, payout.Status);
        Assert.Equal("BANK-REF-1", payout.BankReferenceCode);
    }

    [Fact]
    public async Task A_worker_skips_its_cycle_while_another_instance_holds_the_lock()
    {
        var escrow = await AddEscrowAsync(EscrowStatus.Disputed);
        var (disputeId, _) = await AddHarvestRequestAsync(escrow);
        var gate = new FakeGate();
        await using var provider = Services(gate, new FakeSettlement());
        var worker = provider.GetRequiredService<DisputeHarvestWorker>();
        var key = WorkerLockKey(nameof(DisputeHarvestWorker));

        await using (var otherInstance = new NpgsqlConnection(f.Database.CoreConnection))
        {
            await otherInstance.OpenAsync();
            Assert.True(await TryLockAsync(otherInstance, key));

            Assert.Equal(0, await worker.HarvestPendingAsync());
            Assert.Equal(0, gate.Calls);
        }

        await worker.HarvestPendingAsync();
        await using var db = f.NewContext();
        Assert.NotNull((await db.Disputes.AsNoTracking().SingleAsync(row => row.Id == disputeId)).HarvestedAt);
    }

    [Fact]
    public async Task A_worker_gives_the_lock_back_after_its_cycle()
    {
        await using var provider = Services(new FakeGate(), new FakeSettlement());
        await provider.GetRequiredService<DisputeHarvestWorker>().HarvestPendingAsync();
        await provider.GetRequiredService<AutomaticSettlementWorker>().ProcessAutomaticPayoutsAsync();

        await using var otherInstance = new NpgsqlConnection(f.Database.CoreConnection);
        await otherInstance.OpenAsync();
        Assert.True(await TryLockAsync(otherInstance, WorkerLockKey(nameof(DisputeHarvestWorker))));
        Assert.True(await TryLockAsync(otherInstance, WorkerLockKey(nameof(AutomaticSettlementWorker))));
    }

    private ServiceProvider Services(FakeGate gate, FakeSettlement settlement)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TicketShieldDbContext>(options => options.UseNpgsql(f.PooledConnection));
        services.AddScoped<ITicketShieldDbContext>(provider => provider.GetRequiredService<TicketShieldDbContext>());
        services.AddScoped<IEscrowSettlementCas, EscrowSettlementCas>();
        services.AddScoped<IEscrowPayoutSettler, EscrowPayoutSettler>();
        services.AddScoped<IPayoutReportApplier, PayoutReportApplier>();
        services.AddSingleton<IGateAccessLogClient>(gate);
        services.AddSingleton<ISettlementClient>(settlement);
        services.AddSingleton<PayoutOutboxDispatcher>();
        services.AddSingleton<DisputeHarvestWorker>();
        services.AddSingleton<AutomaticSettlementWorker>();
        services.AddSingleton<StuckSettlementWorker>();
        return services.BuildServiceProvider();
    }

    private async Task<EscrowTransaction> AddEscrowAsync(EscrowStatus status, Action<EscrowTransaction>? configure = null)
    {
        var code = "WRK-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var listing = new ResaleListing
        {
            EventId = WorkerDatabaseFixture.Event,
            TierId = WorkerDatabaseFixture.Tier,
            SellerId = WorkerDatabaseFixture.Seller,
            OriginalTicketCode = code,
            OriginalPrice = 2400000m,
            ResalePrice = 2200000m,
            ListingStatus = ListingStatus.Sold,
            SeatZone = "VIP Zone A"
        };
        var escrow = new EscrowTransaction
        {
            ListingId = listing.Id,
            BuyerId = WorkerDatabaseFixture.Buyer,
            SellerId = WorkerDatabaseFixture.Seller,
            OriginalTicketPrice = 2200000m,
            BuyerFee = 200000m,
            SellerFee = 100000m,
            TotalBuyerPaid = 2400000m,
            NetSellerPayout = 2100000m,
            Status = status,
            UnlockAt = now.AddDays(2),
            DisputeDeadline = now.AddDays(7),
            TransferredAt = now.AddHours(-2),
            NewTicketCode = code,
            QrCodeData = code,
            RecipientName = "Pass Holder",
            RecipientEmail = "buyer@ticketshield.vn"
        };
        configure?.Invoke(escrow);

        await using var db = f.NewContext();
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();
        return escrow;
    }

    private async Task<(Guid DisputeId, Guid MessageId)> AddHarvestRequestAsync(EscrowTransaction escrow)
    {
        var dispute = new Dispute
        {
            EscrowId = escrow.Id,
            BuyerId = escrow.BuyerId,
            DisputeCode = "DP-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            Description = "Vé bị từ chối ở cổng"
        };
        var message = new OutboxMessage
        {
            EventType = nameof(DisputeHarvestRequested),
            Payload = JsonSerializer.Serialize(new DisputeHarvestRequested { DisputeId = dispute.Id })
        };

        await using var db = f.NewContext();
        db.Disputes.Add(dispute);
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return (dispute.Id, message.Id);
    }

    private async Task<Guid> AddPayoutRequestAsync(Guid escrowId, DateTimeOffset? processedAt)
    {
        var message = new OutboxMessage
        {
            EventType = nameof(PayoutRequestedEvent),
            Payload = JsonSerializer.Serialize(new PayoutRequestedEvent
            {
                EscrowId = escrowId,
                SellerId = WorkerDatabaseFixture.Seller,
                Amount = 2100000m,
                IdempotencyKey = PayoutIdempotency.Key(escrowId, 0),
                BankCode = "VCB",
                AccountNumber = "0123456789",
                AccountName = "NGUYEN VAN SELLER"
            }),
            ProcessedAt = processedAt
        };

        await using var db = f.NewContext();
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    // Same derivation as the workers' private ComputeWorkerLockKey.
    private static long WorkerLockKey(string workerName) =>
        BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("ts:worker:" + workerName)), 0);

    private static async Task<bool> TryLockAsync(NpgsqlConnection connection, long key)
    {
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", key);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private sealed class FakeGate : IGateAccessLogClient
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<GateScan>> GetScansAsync(string ticketCode, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<GateScan>>(Array.Empty<GateScan>());
        }
    }

    private sealed class FakeSettlement : ISettlementClient
    {
        public List<PayoutRequestedEvent> Sent { get; } = [];
        public SettlementTransferStatus? Status { get; init; }

        public Task<bool> SendAsync(PayoutRequestedEvent command, CancellationToken cancellationToken = default)
        {
            Sent.Add(command);
            return Task.FromResult(true);
        }

        public Task<SettlementTransferStatus?> GetStatusAsync(string idempotencyKey, CancellationToken cancellationToken = default)
            => Task.FromResult(Status);
    }
}
