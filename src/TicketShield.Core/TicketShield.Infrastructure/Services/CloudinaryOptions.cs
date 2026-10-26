namespace TicketShield.Infrastructure.Services;

/// <summary>
/// Khoá Cloudinary để cất ảnh bằng chứng khiếu nại. Giá trị thật nằm ở Doppler, appsettings chỉ để trống.
/// </summary>
public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}
