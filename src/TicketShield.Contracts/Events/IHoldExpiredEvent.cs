namespace TicketShield.Contracts.Events;

/// <summary>
/// Event lên lịch trễ (Delayed/Scheduled Message) gửi sau khi hết thời gian giữ chỗ (mặc định 10 phút).
/// Khi được tiêu thụ, nếu Escrow vẫn còn Pending, hệ thống sẽ tự động hủy Escrow và mở bán lại Listing.
/// </summary>
public interface IHoldExpiredEvent
{
    Guid EscrowId { get; }
    Guid ListingId { get; }
    DateTimeOffset ScheduledAt { get; }
}
