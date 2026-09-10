namespace TicketShield.Infrastructure.ExternalServices.Organizer;

/// <summary>
/// Ánh xạ định danh sự kiện và hạng vé giữa hệ thống Nhà tổ chức và TicketShield
/// </summary>
public sealed class CoreTicketMapping
{
    public string ExternalEventId { get; set; } = string.Empty;
    public string ExternalTierId { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public Guid TierId { get; set; }
}
