using System.Collections.Concurrent;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;

namespace TicketShield.Infrastructure.Services;

public class MockNapasPayoutGateway : IPayoutGateway
{
    private readonly ConcurrentDictionary<string, PayoutGatewayResult> _transfers = new();

    public Task<PayoutGatewayResult> TransferAsync(PayoutTransferRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"IDEMP-{request.EscrowId}-{request.RetryCount}";

        if (_transfers.TryGetValue(key, out var existing))
        {
            return Task.FromResult(Copy(existing, alreadyTransferred: true));
        }

        var created = new PayoutGatewayResult
        {
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
        IdempotencyKey = source.IdempotencyKey,
        BankReferenceCode = source.BankReferenceCode,
        Amount = source.Amount,
        AlreadyTransferred = alreadyTransferred
    };
}
