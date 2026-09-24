using Microsoft.AspNetCore.SignalR;
using TicketShield.API.Hubs;
using TicketShield.Application.Common.Interfaces;

namespace TicketShield.API.Services;

public class SignalRPaymentRealtimeNotifier : IPaymentRealtimeNotifier
{
    private readonly IHubContext<PaymentHub> _hubContext;

    public SignalRPaymentRealtimeNotifier(IHubContext<PaymentHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyPaymentApprovedAsync(Guid listingId, object payload, CancellationToken cancellationToken = default)
    {
        var group = $"listing_{listingId.ToString().ToLowerInvariant()}";
        await _hubContext.Clients.Group(group).SendAsync("PaymentApproved", payload, cancellationToken);

        var paymentRef = ExtractPaymentReference(payload);
        if (!string.IsNullOrWhiteSpace(paymentRef))
        {
            await _hubContext.Clients.Group($"payment_{paymentRef.Trim().ToUpperInvariant()}").SendAsync("PaymentApproved", payload, cancellationToken);
        }
    }

    public async Task NotifyOrderSettledAsync(Guid listingId, object payload, CancellationToken cancellationToken = default)
    {
        var group = $"listing_{listingId.ToString().ToLowerInvariant()}";
        await _hubContext.Clients.Group(group).SendAsync("OrderSettled", payload, cancellationToken);

        var paymentRef = ExtractPaymentReference(payload);
        if (!string.IsNullOrWhiteSpace(paymentRef))
        {
            await _hubContext.Clients.Group($"payment_{paymentRef.Trim().ToUpperInvariant()}").SendAsync("OrderSettled", payload, cancellationToken);
        }
    }

    public async Task NotifyHoldExpiredAsync(Guid listingId, object payload, CancellationToken cancellationToken = default)
    {
        var group = $"listing_{listingId.ToString().ToLowerInvariant()}";
        await _hubContext.Clients.Group(group).SendAsync("HoldExpired", payload, cancellationToken);

        var paymentRef = ExtractPaymentReference(payload);
        if (!string.IsNullOrWhiteSpace(paymentRef))
        {
            await _hubContext.Clients.Group($"payment_{paymentRef.Trim().ToUpperInvariant()}").SendAsync("HoldExpired", payload, cancellationToken);
        }
    }

    private static string? ExtractPaymentReference(object? payload)
    {
        if (payload == null)
        {
            return null;
        }

        var prop = payload.GetType().GetProperty("paymentReference", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        return prop?.GetValue(payload)?.ToString();
    }
}
