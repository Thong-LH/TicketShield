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

    public async Task JoinPayment(string paymentReference)
    {
        if (!string.IsNullOrWhiteSpace(paymentReference))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"payment_{paymentReference.Trim().ToUpperInvariant()}");
        }
    }

    public async Task LeavePayment(string paymentReference)
    {
        if (!string.IsNullOrWhiteSpace(paymentReference))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"payment_{paymentReference.Trim().ToUpperInvariant()}");
        }
    }
}
