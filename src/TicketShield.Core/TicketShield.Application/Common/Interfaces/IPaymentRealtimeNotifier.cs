namespace TicketShield.Application.Common.Interfaces;

public interface IPaymentRealtimeNotifier
{
    Task NotifyPaymentApprovedAsync(Guid listingId, object payload, CancellationToken cancellationToken = default);
    Task NotifyOrderSettledAsync(Guid listingId, object payload, CancellationToken cancellationToken = default);
    Task NotifyHoldExpiredAsync(Guid listingId, object payload, CancellationToken cancellationToken = default);
}
