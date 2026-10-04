namespace TicketShield.Application.Common.Interfaces;

public interface IPayoutRealtimeNotifier
{
    Task NotifyPayoutCompletedAsync(Guid sellerId, Guid escrowId, Guid? listingId, object payload, CancellationToken cancellationToken = default);
}
