using Microsoft.EntityFrameworkCore;
using TicketShield.Settlement.API.Data;
using TicketShield.Settlement.API.Gateway;
using TicketShield.Settlement.API.Payouts;

namespace TicketShield.UnitTests.Services;

public class SettlementPayoutServiceTests
{
    [Fact]
    public async Task Accept_WhenSnapshotIsIncomplete_DoesNotTransfer()
    {
        await using var db = await CreateDb();
        var gateway = new MockNapasPayoutGateway();
        var service = Service(db, gateway);

        var result = await service.AcceptAsync(Command(accountNumber: "", amount: 0));

        Assert.False(result.Accepted);
        Assert.False(result.GatewayCalled);
        Assert.False(await db.TransferOrders.AnyAsync());
    }

    [Fact]
    public async Task Accept_WhenTheSameKeyAlreadyHasAReceipt_DoesNotCallTheGatewayAgain()
    {
        await using var db = await CreateDb();
        var gateway = new MockNapasPayoutGateway();
        var service = Service(db, gateway);
        var command = Command();

        var first = await service.AcceptAsync(command);
        var second = await service.AcceptAsync(command);

        Assert.True(first.GatewayCalled);
        Assert.False(second.GatewayCalled);
        Assert.Equal(first.BankReference, second.BankReference);
        Assert.Equal(1, await db.TransferOrders.CountAsync());
    }

    [Fact]
    public void WaitAfterAttempt_GrowsForEachTimeout()
    {
        var first = TimeSpan.FromSeconds(1);
        Assert.True(SettlementPayoutService.WaitAfterAttempt(2, first) > SettlementPayoutService.WaitAfterAttempt(1, first));
    }

    [Fact]
    public async Task Retry_WhenTheBankTimesOutThreeTimes_StopsOnTheSameKey()
    {
        await using var db = await CreateDb();
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.NewGuid();
        gateway.TimeoutNext(escrowId, 3);
        var service = Service(db, gateway, TimeSpan.Zero);
        var command = Command(escrowId);

        await service.AcceptAsync(command);
        await service.RetryDueAsync(DateTimeOffset.UtcNow.AddMinutes(1));
        await service.RetryDueAsync(DateTimeOffset.UtcNow.AddMinutes(2));
        await service.RetryDueAsync(DateTimeOffset.UtcNow.AddMinutes(3));

        var order = await db.TransferOrders.SingleAsync();
        Assert.Equal(TransferState.Exhausted, order.State);
        Assert.Equal(3, order.AttemptCount);
        Assert.Equal(command.RetryCount, order.RetryNumber);
        Assert.Null(order.BankReference);
    }

    [Fact]
    public async Task Accept_WhenTheAccountIsRejected_DoesNotRetry()
    {
        await using var db = await CreateDb();
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.NewGuid();
        gateway.RejectAccount(escrowId);
        var service = Service(db, gateway);
        var command = Command(escrowId);

        var first = await service.AcceptAsync(command);
        var second = await service.AcceptAsync(command);

        Assert.Equal(TransferState.Failed, first.State);
        Assert.False(second.GatewayCalled);
        Assert.Equal(1, (await db.TransferOrders.SingleAsync()).AttemptCount);
    }

    private static SettlementPayoutService Service(SettlementDbContext db, MockNapasPayoutGateway gateway, TimeSpan? wait = null)
        => new(db, gateway, new AcceptingReporter(), wait);

    private static async Task<SettlementDbContext> CreateDb()
    {
        var options = new DbContextOptionsBuilder<SettlementDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new SettlementDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static PayoutCommand Command(Guid? escrowId = null, string accountNumber = "0938434102", decimal amount = 100_000m)
    {
        var id = escrowId ?? Guid.NewGuid();
        return new PayoutCommand
        {
            EscrowId = id,
            SellerId = Guid.NewGuid(),
            Amount = amount,
            RetryCount = 0,
            IdempotencyKey = PayoutIdempotency.Key(id, 0),
            BankCode = "MB",
            AccountNumber = accountNumber,
            AccountName = "SELLER"
        };
    }

    private sealed class AcceptingReporter : ICorePayoutReporter
    {
        public Task<bool> ReportAsync(Guid escrowId, string idempotencyKey, bool succeeded, string? bankReference, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
