namespace TicketShield.Infrastructure.ExternalServices.Organizer;

/// <summary>
/// Cấu hình kết nối gRPC bảo mật tới hệ thống Nhà tổ chức
/// </summary>
public sealed class OrganizerConnectionOptions
{
    public bool Enabled { get; set; }
    public string Address { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string HmacKey { get; set; } = string.Empty;
    public string OrganizerId { get; set; } = string.Empty;
    public int DeadlineSeconds { get; set; } = 15;
    public Dictionary<string, CoreTicketMapping> Mappings { get; set; } = new(StringComparer.Ordinal);
}
