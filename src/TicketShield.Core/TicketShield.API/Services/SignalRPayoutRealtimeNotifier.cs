using Microsoft.AspNetCore.SignalR;
using TicketShield.API.Hubs;
using TicketShield.Application.Common.Interfaces;

namespace TicketShield.API.Services;

public class SignalRPayoutRealtimeNotifier : IPayoutRealtimeNotifier
{
    private readonly IHubContext<PaymentHub> _hubContext;

    public SignalRPayoutRealtimeNotifier(IHubContext<PaymentHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyPayoutCompletedAsync(Guid sellerId, Guid escrowId, Guid? listingId, object payload, CancellationToken cancellationToken = default)
    {
        var sellerGroup = $"seller_{sellerId.ToString().ToLowerInvariant()}";
        await _hubContext.Clients.Group(sellerGroup).SendAsync("PayoutCompleted", payload, cancellationToken);

        var userGroup = $"user_{sellerId.ToString().ToLowerInvariant()}";
        await _hubContext.Clients.Group(userGroup).SendAsync("PayoutCompleted", payload, cancellationToken);

        if (listingId.HasValue && listingId.Value != Guid.Empty)
        {
            var listingGroup = $"listing_{listingId.Value.ToString().ToLowerInvariant()}";
            await _hubContext.Clients.Group(listingGroup).SendAsync("PayoutCompleted", payload, cancellationToken);
        }

        try
        {
            await _hubContext.Clients.User(sellerId.ToString()).SendAsync("PayoutCompleted", payload, cancellationToken);
        }
        catch
        {
            // Ignore if User identifier resolution is unavailable
        }
    }
}
