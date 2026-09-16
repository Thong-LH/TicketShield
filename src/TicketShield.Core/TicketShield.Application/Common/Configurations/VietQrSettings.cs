namespace TicketShield.Application.Common.Configurations;

public class VietQrSettings
{
    public const string SectionName = "VietQR";

    public string Provider { get; set; } = "SePAY";
    public string BaseUrl { get; set; } = "https://my.sepay.vn/userapi";
    public string ApiKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string BankBin { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string QrTemplate { get; set; } = "compact2";
}
