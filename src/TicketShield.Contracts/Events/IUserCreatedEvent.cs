namespace TicketShield.Contracts.Events;

/// <summary>
/// Event phát ra từ Identity Microservice khi một tài khoản người dùng mới được tạo.
/// Trading Core sẽ lắng nghe event này để đồng bộ hóa tự động vào bảng shadow_users (Idempotent Consumer).
/// </summary>
public interface IUserCreatedEvent
{
    Guid UserId { get; }
    string Email { get; }
    string FullName { get; }
    string? PhoneNumber { get; }
    string Role { get; }
    DateTimeOffset CreatedAt { get; }
}

public record UserCreatedEvent(
    Guid UserId,
    string Email,
    string FullName,
    string? PhoneNumber,
    string Role,
    DateTimeOffset CreatedAt
) : IUserCreatedEvent;
