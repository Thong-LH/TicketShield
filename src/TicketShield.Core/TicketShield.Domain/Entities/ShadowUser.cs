using TicketShield.Domain.Common;
using TicketShield.Domain.Enums;

namespace TicketShield.Domain.Entities;

/// <summary>
/// Bảng bóng lưu trữ thông tin cơ bản của người dùng tại Trading Core phục vụ đọc nhanh (Read-Model / CQRS).
/// Được đồng bộ tự động từ Identity Service qua RabbitMQ Event Bus.
/// </summary>
public class ShadowUser : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public bool IsActive { get; set; } = true;
    public string PayoutBankCode { get; set; } = string.Empty;
    public string PayoutAccountNumber { get; set; } = string.Empty;
    public string PayoutAccountName { get; set; } = string.Empty;

    // Navigation properties phục vụ query Marketplace & Escrow trong Core
    public ICollection<ResaleListing> ResaleListings { get; set; } = new List<ResaleListing>();
}
