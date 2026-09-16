namespace TicketShield.Contracts.Events;

/// <summary>
/// Event phát ra từ Identity Microservice khi thông tin người dùng có sự thay đổi.
/// Trading Core sẽ lắng nghe event này để cập nhật bản ghi trong bảng shadow_users.
/// </summary>
public interface IUserProfileUpdatedEvent
{
    Guid UserId { get; }
    string FullName { get; }
    string? PhoneNumber { get; }
    string Role { get; }
    bool IsActive { get; }
    DateTimeOffset UpdatedAt { get; }
}

public record UserProfileUpdatedEvent(
    Guid UserId,
    string FullName,
    string? PhoneNumber,
    string Role,
    bool IsActive,
    DateTimeOffset UpdatedAt
) : IUserProfileUpdatedEvent;
