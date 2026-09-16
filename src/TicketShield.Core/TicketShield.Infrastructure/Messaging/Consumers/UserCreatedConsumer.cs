using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Contracts.Events;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Messaging.Consumers;

/// <summary>
/// Idempotent Consumer đồng bộ dữ liệu người dùng từ Identity Microservice sang bảng shadow_users trong Trading Core
/// </summary>
public class UserCreatedConsumer : IConsumer<IUserCreatedEvent>
{
    private readonly ITicketShieldDbContext _context;
    private readonly ILogger<UserCreatedConsumer> _logger;

    public UserCreatedConsumer(ITicketShieldDbContext context, ILogger<UserCreatedConsumer> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IUserCreatedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("[EventBus] Nhận UserCreatedEvent cho UserId: {UserId}, Email: {Email}", msg.UserId, msg.Email);

        var existingShadowUser = await _context.ShadowUsers
            .FirstOrDefaultAsync(u => u.Id == msg.UserId);

        var roleEnum = Enum.TryParse<UserRole>(msg.Role, true, out var r) ? r : UserRole.User;

        if (existingShadowUser == null)
        {
            var shadowUser = new ShadowUser
            {
                Id = msg.UserId,
                Email = msg.Email,
                FullName = msg.FullName,
                PhoneNumber = msg.PhoneNumber,
                Role = roleEnum,
                IsActive = true,
                CreatedAt = msg.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _context.ShadowUsers.Add(shadowUser);
        }
        else
        {
            existingShadowUser.Email = msg.Email;
            existingShadowUser.FullName = msg.FullName;
            existingShadowUser.PhoneNumber = msg.PhoneNumber;
            existingShadowUser.Role = roleEnum;
            existingShadowUser.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _context.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("[EventBus] Đã đồng bộ thành công ShadowUser cho UserId: {UserId}", msg.UserId);
    }
}
