using TicketShield.Domain.Common;

namespace TicketShield.Domain.Entities;

public class SystemSetting : BaseEntity
{
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public string DataType { get; set; } = "Decimal"; // 'Decimal', 'Money', etc.
    public string? Description { get; set; }
    public Guid? UpdatedBy { get; set; }
}
