using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Contracts.Events;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Messaging.Consumers;

public class UserProfileUpdatedConsumer : IConsumer<IUserProfileUpdatedEvent>
{
    private readonly ITicketShieldDbContext _context;
    private readonly ILogger<UserProfileUpdatedConsumer> _logger;

    public UserProfileUpdatedConsumer(ITicketShieldDbContext context, ILogger<UserProfileUpdatedConsumer> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IUserProfileUpdatedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("[EventBus] Received UserProfileUpdatedEvent for UserId: {UserId}", msg.UserId);

        var existingShadowUser = await _context.ShadowUsers
            .FirstOrDefaultAsync(u => u.Id == msg.UserId, context.CancellationToken);

        var roleEnum = Enum.TryParse<UserRole>(msg.Role, true, out var r) ? r : UserRole.User;

        if (existingShadowUser != null)
        {
            existingShadowUser.FullName = msg.FullName;
            existingShadowUser.PhoneNumber = msg.PhoneNumber;
            existingShadowUser.Role = roleEnum;
            existingShadowUser.IsActive = msg.IsActive;
            existingShadowUser.UpdatedAt = msg.UpdatedAt;

            await _context.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("[EventBus] Successfully updated ShadowUser for UserId: {UserId}", msg.UserId);
        }
        else
        {
            _logger.LogWarning("[EventBus] ShadowUser not found for UserId: {UserId} to update profile", msg.UserId);
        }
    }
}
