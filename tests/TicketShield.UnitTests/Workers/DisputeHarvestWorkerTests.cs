using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Disputes;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Workers;

namespace TicketShield.UnitTests.Workers;

public class DisputeHarvestWorkerTests
{
    [Fact]
    public async Task Harvest_WhenOrganizerFails_KeepsTheDisputeAndLeavesTheOutboxPending()
    {
        var fake = new FakeGate { Error = new HttpRequestException("down") };
        var (provider, disputeId) = await SeedAsync(fake, "NEW-1", DateTimeOffset.UtcNow);
        var worker = provider.GetRequiredService<DisputeHarvestWorker>();

        var finished = await worker.HarvestPendingAsync();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketShieldDbContext>();
        var dispute = await db.Disputes.AsNoTracking().SingleAsync();
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync();
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(0, finished);
        Assert.Equal(disputeId, dispute.Id);
        Assert.Equal(EscrowStatus.Disputed, escrow.Status);
        Assert.Null(dispute.HarvestedAt);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task Harvest_WhenTheGateHasNoScans_CompletesWithAnEmptyLabel()
    {
        var fake = new FakeGate();
        var (provider, _) = await SeedAsync(fake, "NEW-1", DateTimeOffset.UtcNow);
        var worker = provider.GetRequiredService<DisputeHarvestWorker>();

        await worker.HarvestPendingAsync();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketShieldDbContext>();
        var dispute = await db.Disputes.AsNoTracking().SingleAsync();
        Assert.NotNull(dispute.HarvestedAt);
        Assert.Null(dispute.Recommendation);
        Assert.NotNull((await db.OutboxMessages.AsNoTracking().SingleAsync()).ProcessedAt);
    }

    [Fact]
    public async Task Harvest_WhenRunAgain_DoesNotReplaceTheStoredScans()
    {
        var fake = new FakeGate();
        var (provider, _) = await SeedAsync(fake, "NEW-1", DateTimeOffset.UtcNow);
        var worker = provider.GetRequiredService<DisputeHarvestWorker>();
        await worker.HarvestPendingAsync();

        string? snapshot;
        await using (var read = provider.CreateAsyncScope())
        {
            var db = read.ServiceProvider.GetRequiredService<TicketShieldDbContext>();
            snapshot = (await db.Disputes.SingleAsync()).GateLogSnapshot;
            var message = await db.OutboxMessages.SingleAsync();
            message.ProcessedAt = null;
            await db.SaveChangesAsync();
        }

        fake.Next = code => new[]
        {
            new GateScan { TicketCode = code, ScannedAt = DateTimeOffset.UtcNow, ScanResult = "SUCCESS", GateName = "Cổng A" }
        };
        await worker.HarvestPendingAsync();

        await using var checkScope = provider.CreateAsyncScope();
        var check = checkScope.ServiceProvider.GetRequiredService<TicketShieldDbContext>();
        var dispute = await check.Disputes.AsNoTracking().SingleAsync();
        Assert.Equal(snapshot, dispute.GateLogSnapshot);
        Assert.Null(dispute.Recommendation);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task Harvest_WhenTheBuyerEntered_StoresRejectFraud()
    {
        var transferred = DateTimeOffset.UtcNow.AddHours(-1);
        var fake = new FakeGate
        {
            Next = code => new[]
            {
                new GateScan
                {
                    TicketCode = code,
                    ScannedAt = transferred.AddMinutes(10),
                    ScanResult = "SUCCESS",
                    GateName = "Cổng A",
                    ScannerDeviceId = "dev-a"
                }
            }
        };
        var (provider, _) = await SeedAsync(fake, "NEW-1", transferred);
        await provider.GetRequiredService<DisputeHarvestWorker>().HarvestPendingAsync();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketShieldDbContext>();
        var dispute = await db.Disputes.AsNoTracking().SingleAsync();
        Assert.Equal(DisputeRecommendation.RecommendRejectFraud, dispute.Recommendation);
        Assert.Contains("SUCCESS", dispute.GateLogSnapshot);
        Assert.NotNull(dispute.HarvestedAt);
    }

    [Fact]
    public async Task Harvest_WhenTheNewTicketCodeIsMissing_CompletesWithoutCallingTheOrganizer()
    {
        var fake = new FakeGate();
        var (provider, _) = await SeedAsync(fake, null, DateTimeOffset.UtcNow);
        await provider.GetRequiredService<DisputeHarvestWorker>().HarvestPendingAsync();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketShieldDbContext>();
        var dispute = await db.Disputes.AsNoTracking().SingleAsync();
        Assert.NotNull(dispute.HarvestedAt);
        Assert.Null(dispute.Recommendation);
        Assert.Equal(0, fake.Calls);
    }

    private static async Task<(ServiceProvider Provider, Guid DisputeId)> SeedAsync(
        FakeGate fake,
        string? newTicketCode,
        DateTimeOffset? transferredAt)
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<TicketShieldDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<ITicketShieldDbContext>(provider => provider.GetRequiredService<TicketShieldDbContext>());
        services.AddSingleton<IGateAccessLogClient>(fake);
        services.AddSingleton<DisputeHarvestWorker>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var disputeId = Guid.NewGuid();
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = EscrowStatus.Disputed,
            NewTicketCode = newTicketCode,
            TransferredAt = transferredAt
        };
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketShieldDbContext>();
        db.EscrowTransactions.Add(escrow);
        db.Disputes.Add(new Dispute
        {
            Id = disputeId,
            EscrowId = escrow.Id,
            BuyerId = escrow.BuyerId,
            DisputeCode = "DP-HARVEST",
            Status = DisputeStatus.Open,
            Description = "Vé bị từ chối"
        });
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = nameof(DisputeHarvestRequested),
            Payload = System.Text.Json.JsonSerializer.Serialize(new DisputeHarvestRequested { DisputeId = disputeId })
        });
        await db.SaveChangesAsync();
        return (provider, disputeId);
    }

    private sealed class FakeGate : IGateAccessLogClient
    {
        public int Calls { get; private set; }
        public Exception? Error { get; set; }
        public Func<string, IReadOnlyList<GateScan>>? Next { get; set; }

        public Task<IReadOnlyList<GateScan>> GetScansAsync(string ticketCode, CancellationToken cancellationToken)
        {
            Calls++;
            if (Error != null)
            {
                throw Error;
            }

            IReadOnlyList<GateScan> scans = Next?.Invoke(ticketCode) ?? Array.Empty<GateScan>();
            return Task.FromResult(scans);
        }
    }
}
