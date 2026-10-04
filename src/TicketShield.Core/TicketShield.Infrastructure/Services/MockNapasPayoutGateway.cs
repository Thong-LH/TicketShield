using System.Collections.Concurrent;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;

namespace TicketShield.Infrastructure.Services;

public class MockNapasPayoutGateway : IPayoutGateway
{
    private readonly ConcurrentDictionary<string, PayoutGatewayResult> _transfers = new();
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

    public Task<PayoutGatewayResult> TransferAsync(PayoutTransferRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = PayoutIdempotency.Key(request.EscrowId, request.RetryCount);

        if (_transfers.TryGetValue(key, out var existing))
        {
            return Task.FromResult(Copy(existing, alreadyTransferred: true));
        }

        if (_rejectedAccounts.ContainsKey(request.EscrowId))
        {
            return Task.FromResult(new PayoutGatewayResult
            {
                Outcome = PayoutGatewayOutcome.InvalidAccount,
                IdempotencyKey = key,
                Amount = request.Amount
            });
        }

        if (_timeoutsRemaining.TryGetValue(request.EscrowId, out var remaining) && remaining > 0)
        {
            _timeoutsRemaining[request.EscrowId] = remaining - 1;
            return Task.FromResult(new PayoutGatewayResult
            {
                Outcome = PayoutGatewayOutcome.TimedOut,
                IdempotencyKey = key,
                Amount = request.Amount
            });
        }

        var created = new PayoutGatewayResult
        {
            Outcome = PayoutGatewayOutcome.Succeeded,
            IdempotencyKey = key,
            BankReferenceCode = $"FT{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            Amount = request.Amount
        };

        if (_transfers.TryAdd(key, created))
        {
            return Task.FromResult(Copy(created, alreadyTransferred: false));
        }

        return Task.FromResult(Copy(_transfers[key], alreadyTransferred: true));
    }

    private static PayoutGatewayResult Copy(PayoutGatewayResult source, bool alreadyTransferred) => new()
    {
        Outcome = source.Outcome,
        IdempotencyKey = source.IdempotencyKey,
        BankReferenceCode = source.BankReferenceCode,
        Amount = source.Amount,
        AlreadyTransferred = alreadyTransferred
    };
}
