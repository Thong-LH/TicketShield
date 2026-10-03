using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Workers;

namespace TicketShield.UnitTests.Workers;

public class AutomaticSettlementWorkerTests
{
    private sealed class RecordingSettler : IEscrowPayoutSettler
    {
        public List<Guid> SettledIds { get; } = [];

        public Task<bool> TrySettleAsync(Guid escrowId, CancellationToken cancellationToken = default)
        {
            SettledIds.Add(escrowId);
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task ProcessAutomaticPayouts_SettlesOnlyEscrowsWhoseUnlockTimeHasPassed()
    {
        var databaseName = Guid.NewGuid().ToString();
        var settler = new RecordingSettler();
        var services = new ServiceCollection();
        services.AddDbContext<TicketShieldDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<ITicketShieldDbContext>(provider => provider.GetRequiredService<TicketShieldDbContext>());
        services.AddSingleton<IEscrowPayoutSettler>(settler);
        var provider = services.BuildServiceProvider();

        var dueId = Guid.NewGuid();
        var waitingId = Guid.NewGuid();
        await using (var db = provider.GetRequiredService<TicketShieldDbContext>())
        {
            db.EscrowTransactions.AddRange(
                new EscrowTransaction
                {
                    Id = dueId,
                    ListingId = Guid.NewGuid(),
                    BuyerId = Guid.NewGuid(),
                    SellerId = Guid.NewGuid(),
                    Status = EscrowStatus.Locked,
                    InSettlementBuffer = true,
                    UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1)
                },
                new EscrowTransaction
                {
                    Id = waitingId,
                    ListingId = Guid.NewGuid(),
                    BuyerId = Guid.NewGuid(),
                    SellerId = Guid.NewGuid(),
                    Status = EscrowStatus.Locked,
                    InSettlementBuffer = true,
                    UnlockAt = DateTimeOffset.UtcNow.AddHours(2)
                });
            await db.SaveChangesAsync();
        }

        var worker = new AutomaticSettlementWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AutomaticSettlementWorker>.Instance,
            TimeSpan.FromSeconds(60));

        var settled = await worker.ProcessAutomaticPayoutsAsync();

        Assert.Equal(1, settled);
        Assert.Equal([dueId], settler.SettledIds);
    }
}
