using MassTransit;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Contracts.Events;
using TicketShield.Infrastructure.Messaging.Events;

namespace TicketShield.Infrastructure.Messaging.Services;

public class MassTransitMessageSchedulerService : IMessageSchedulerService
{
    private readonly IBus _bus;
    private readonly ILogger<MassTransitMessageSchedulerService> _logger;
    private readonly IMessageScheduler? _scheduler;

    public MassTransitMessageSchedulerService(
        IBus bus,
        ILogger<MassTransitMessageSchedulerService> logger,
        IMessageScheduler? scheduler = null)
    {
        _bus = bus;
        _logger = logger;
        _scheduler = scheduler;
    }

    public async Task ScheduleHoldExpiryAsync(
        Guid escrowId,
        Guid listingId,
        DateTimeOffset expireAt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var eventMessage = new HoldExpiredEvent
            {
                EscrowId = escrowId,
                ListingId = listingId,
                ScheduledAt = DateTimeOffset.UtcNow
            };

            if (_scheduler != null)
            {
                await _scheduler.SchedulePublish<IHoldExpiredEvent>(
                    expireAt.UtcDateTime,
                    eventMessage,
                    cancellationToken);

                _logger.LogInformation(
                    "Scheduled IHoldExpiredEvent via IMessageScheduler for Escrow {EscrowId} at {ExpireAt}",
                    escrowId, expireAt);
            }
            else
            {
                _logger.LogInformation(
                    "Registered hold expiry for Escrow {EscrowId} on Listing {ListingId} at {ExpireAt} (monitored by ExpiredHoldReleaseWorker)",
                    escrowId, listingId, expireAt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule IHoldExpiredEvent for Escrow {EscrowId}", escrowId);
        }
    }
}
