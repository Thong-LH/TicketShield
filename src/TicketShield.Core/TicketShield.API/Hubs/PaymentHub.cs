using Microsoft.AspNetCore.SignalR;

namespace TicketShield.API.Hubs;

public class PaymentHub : Hub
{
    public async Task JoinListing(string listingId)
    {
        if (!string.IsNullOrWhiteSpace(listingId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"listing_{listingId.Trim().ToLowerInvariant()}");
        }
    }

    public async Task LeaveListing(string listingId)
    {
        if (!string.IsNullOrWhiteSpace(listingId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"listing_{listingId.Trim().ToLowerInvariant()}");
        }
    }
}
