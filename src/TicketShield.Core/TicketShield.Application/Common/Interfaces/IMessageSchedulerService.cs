namespace TicketShield.Application.Common.Interfaces;

public interface IMessageSchedulerService
{
    Task ScheduleHoldExpiryAsync(Guid escrowId, Guid listingId, DateTimeOffset expireAt, CancellationToken cancellationToken = default);
}
