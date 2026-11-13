namespace TicketShield.Application.Features.Admin.Analytics.Models;

/// <summary>
/// Thống kê tình trạng kho vé Admin (Ticket Inventory) theo các trạng thái vòng đời vé
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-338 (BE-11.1.5)
/// </summary>
public class TicketInventoryAnalyticsDto
{
    /// <summary>
    /// Tổng số lượng vé lưu trữ trong kho hệ thống.
    /// </summary>
    public int TotalTicketsInInventory { get; set; }

    /// <summary>
    /// Số lượng vé hợp lệ sẵn sàng sử dụng hoặc chuyển nhượng (TicketStatus.Valid).
    /// </summary>
    public int ValidTickets { get; set; }

    /// <summary>
    /// Số lượng vé đang bị khóa để đăng bán lại trên sàn (TicketStatus.LockedForResale).
    /// </summary>
    public int LockedForResaleTickets { get; set; }

    /// <summary>
    /// Số lượng vé đã hoàn tất sang tên đổi chủ (TicketStatus.Transferred).
    /// </summary>
    public int TransferredTickets { get; set; }

    /// <summary>
    /// Số lượng vé đã qua cửa quét cổng tại sự kiện (TicketStatus.Used).
    /// </summary>
    public int UsedTickets { get; set; }

    /// <summary>
    /// Số lượng vé đã bị hủy hoặc vô hiệu hóa (TicketStatus.Cancelled).
    /// </summary>
    public int CancelledTickets { get; set; }
}

