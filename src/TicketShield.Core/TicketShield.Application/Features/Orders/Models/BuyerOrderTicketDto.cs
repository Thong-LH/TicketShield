namespace TicketShield.Application.Features.Orders.Models;

/// <summary>
/// Một vé trong đơn hàng của người mua. Đơn Combo có nhiều vé, mỗi vé có mã và khu ghế riêng.
/// Đơn chưa được phát vé mới thì TicketCode là mã đã che (mã gốc vẫn là vé của người bán).
/// </summary>
public class BuyerOrderTicketDto
{
    public string TicketCode { get; set; } = string.Empty;
    public string? SeatZone { get; set; }
}
