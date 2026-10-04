using TicketShield.Application.Common.Models;
using TicketShield.Infrastructure.Services;

namespace TicketShield.UnitTests.Services;

public class MockNapasPayoutGatewayTests
{
    [Fact]
    public async Task Transfer_UsesIdempotencyKeyFromEscrowAndRetry()
    {
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var result = await gateway.TransferAsync(new PayoutTransferRequest
        {
            EscrowId = escrowId,
            RetryCount = 0,
            BankCode = "MB",
            AccountNumber = "0938434102",
            AccountName = "NGUYEN VAN SELLER",
            Amount = 475_000m
        });

        Assert.Equal($"IDEMP-{escrowId}-0", result.IdempotencyKey);
        Assert.Equal(475_000m, result.Amount);
        Assert.False(string.IsNullOrWhiteSpace(result.BankReferenceCode));
        Assert.False(result.AlreadyTransferred);
    }

    [Fact]
    public async Task Transfer_WhenCalledTwiceWithSameKey_ReturnsTheOriginalTransfer()
    {
        var gateway = new MockNapasPayoutGateway();
        var request = new PayoutTransferRequest
        {
            EscrowId = Guid.NewGuid(),
            RetryCount = 1,
            BankCode = "MB",
            AccountNumber = "0938434102",
            AccountName = "NGUYEN VAN SELLER",
            Amount = 200_000m
        };

        var first = await gateway.TransferAsync(request);
        request.Amount = 999_000m;
        var second = await gateway.TransferAsync(request);

        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal(first.BankReferenceCode, second.BankReferenceCode);
        Assert.Equal(200_000m, second.Amount);
        Assert.False(first.AlreadyTransferred);
        Assert.True(second.AlreadyTransferred);
    }

    [Fact]
    public async Task Transfer_WhenRetryCountChanges_StartsASeparateTransfer()
    {
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.NewGuid();
        var first = await gateway.TransferAsync(Request(escrowId, retryCount: 0));
        var retry = await gateway.TransferAsync(Request(escrowId, retryCount: 1));

        Assert.NotEqual(first.IdempotencyKey, retry.IdempotencyKey);
        Assert.NotEqual(first.BankReferenceCode, retry.BankReferenceCode);
        Assert.False(retry.AlreadyTransferred);
    }

    [Fact]
    public async Task Transfer_WhenTimeoutIsQueued_DoesNotStoreAReceipt()
    {
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.NewGuid();
        gateway.TimeoutNext(escrowId, 1);

        var timedOut = await gateway.TransferAsync(Request(escrowId, retryCount: 0));
        var succeeded = await gateway.TransferAsync(Request(escrowId, retryCount: 0));

        Assert.Equal(PayoutGatewayOutcome.TimedOut, timedOut.Outcome);
        Assert.Equal(PayoutGatewayOutcome.Succeeded, succeeded.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(succeeded.BankReferenceCode));
    }

    [Fact]
    public async Task Transfer_WhenAccountIsRejected_ReturnsInvalidAccount()
    {
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.NewGuid();
        gateway.RejectAccount(escrowId);

        var result = await gateway.TransferAsync(Request(escrowId, retryCount: 0));

        Assert.Equal(PayoutGatewayOutcome.InvalidAccount, result.Outcome);
        Assert.True(string.IsNullOrWhiteSpace(result.BankReferenceCode));
    }

    private static PayoutTransferRequest Request(Guid escrowId, int retryCount) => new()
    {
        EscrowId = escrowId,
        RetryCount = retryCount,
        BankCode = "MB",
        AccountNumber = "0938434102",
        AccountName = "NGUYEN VAN SELLER",
        Amount = 100_000m
    };
}
