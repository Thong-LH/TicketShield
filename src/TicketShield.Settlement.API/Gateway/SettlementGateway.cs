using System.Collections.Concurrent;

namespace TicketShield.Settlement.API.Gateway;

public interface ISettlementGateway
{
    Task<GatewayTransferResult> TransferAsync(GatewayTransferRequest request, CancellationToken cancellationToken = default);
}

public class GatewayTransferRequest
{
    public Guid EscrowId { get; set; }
    public int RetryCount { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public enum GatewayOutcome
{
    Succeeded,
    TimedOut,
    InvalidAccount
}

public class GatewayTransferResult
{
    public GatewayOutcome Outcome { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? BankReferenceCode { get; set; }
}

public static class PayoutIdempotency
{
    public static string Key(Guid escrowId, int retryCount) => $"IDEMP-{escrowId}-{retryCount}";
}

public class MockNapasPayoutGateway : ISettlementGateway
{
    private readonly ConcurrentDictionary<string, GatewayTransferResult> _transfers = new();
    private readonly ConcurrentDictionary<Guid, int> _timeoutsRemaining = new();
    private readonly ConcurrentDictionary<Guid, byte> _rejectedAccounts = new();

    public void TimeoutNext(Guid escrowId, int times)
    {
        if (times > 0)
        {
            _timeoutsRemaining[escrowId] = times;
        }
    }

    public void RejectAccount(Guid escrowId) => _rejectedAccounts[escrowId] = 1;

    public Task<GatewayTransferResult> TransferAsync(GatewayTransferRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = PayoutIdempotency.Key(request.EscrowId, request.RetryCount);
        if (_transfers.TryGetValue(key, out var existing))
        {
            return Task.FromResult(Copy(existing));
        }

        if (_rejectedAccounts.ContainsKey(request.EscrowId) || !IsValidAccount(request.AccountNumber))
        {
            return Task.FromResult(new GatewayTransferResult
            {
                Outcome = GatewayOutcome.InvalidAccount,
                IdempotencyKey = key
            });
        }

        if (_timeoutsRemaining.TryGetValue(request.EscrowId, out var remaining) && remaining > 0)
        {
            _timeoutsRemaining[request.EscrowId] = remaining - 1;
            return Task.FromResult(new GatewayTransferResult
            {
                Outcome = GatewayOutcome.TimedOut,
                IdempotencyKey = key
            });
        }

        var created = new GatewayTransferResult
        {
            Outcome = GatewayOutcome.Succeeded,
            IdempotencyKey = key,
            BankReferenceCode = $"FT{Guid.NewGuid():N}"[..10].ToUpperInvariant()
        };
        if (!_transfers.TryAdd(key, created))
        {
            created = _transfers[key];
        }

        return Task.FromResult(Copy(created));
    }

    public static bool IsValidAccount(string accountNumber)
        => accountNumber.Length is >= 6 and <= 19 && accountNumber.All(char.IsDigit);

    private static GatewayTransferResult Copy(GatewayTransferResult source) => new()
    {
        Outcome = source.Outcome,
        IdempotencyKey = source.IdempotencyKey,
        BankReferenceCode = source.BankReferenceCode
    };
}
