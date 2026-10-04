using TicketShield.Settlement.API.Gateway;

namespace TicketShield.UnitTests.Services;

public class MockNapasPayoutGatewayTests
{
    [Fact]
    public async Task Transfer_UsesIdempotencyKeyFromEscrowAndRetry()
    {
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var result = await gateway.TransferAsync(Request(escrowId, 0));

        Assert.Equal($"IDEMP-{escrowId}-0", result.IdempotencyKey);
        Assert.False(string.IsNullOrWhiteSpace(result.BankReferenceCode));
    }

    [Fact]
    public async Task Transfer_WhenCalledTwiceWithSameKey_ReturnsTheOriginalTransfer()
    {
        var gateway = new MockNapasPayoutGateway();
        var request = Request(Guid.NewGuid(), 1);

        var first = await gateway.TransferAsync(request);
        var second = await gateway.TransferAsync(request);

        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal(first.BankReferenceCode, second.BankReferenceCode);
    }

    [Fact]
    public async Task Transfer_WhenRetryCountChanges_StartsASeparateTransfer()
    {
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.NewGuid();
        var first = await gateway.TransferAsync(Request(escrowId, 0));
        var retry = await gateway.TransferAsync(Request(escrowId, 1));

        Assert.NotEqual(first.IdempotencyKey, retry.IdempotencyKey);
        Assert.NotEqual(first.BankReferenceCode, retry.BankReferenceCode);
    }

    [Fact]
    public async Task Transfer_WhenTimeoutIsQueued_DoesNotStoreAReceipt()
    {
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.NewGuid();
        gateway.TimeoutNext(escrowId, 1);

        var timedOut = await gateway.TransferAsync(Request(escrowId, 0));
        var succeeded = await gateway.TransferAsync(Request(escrowId, 0));

        Assert.Equal(GatewayOutcome.TimedOut, timedOut.Outcome);
        Assert.Equal(GatewayOutcome.Succeeded, succeeded.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(succeeded.BankReferenceCode));
    }

    [Fact]
    public async Task Transfer_WhenAccountIsRejected_ReturnsInvalidAccount()
    {
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.NewGuid();
        gateway.RejectAccount(escrowId);

        var result = await gateway.TransferAsync(Request(escrowId, 0));

        Assert.Equal(GatewayOutcome.InvalidAccount, result.Outcome);
        Assert.True(string.IsNullOrWhiteSpace(result.BankReferenceCode));
    }

    private static GatewayTransferRequest Request(Guid escrowId, int retryCount) => new()
    {
        EscrowId = escrowId,
        RetryCount = retryCount,
        AccountNumber = "0938434102",
        Amount = 100_000m
    };
}
