namespace TicketShield.Application.Common.Models;

public class VietQrQuickLinkRequest
{
    public string BankBin { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string TransferContent { get; set; } = string.Empty;
    public string? Template { get; set; }
}

public class VietQrQuickLinkResult
{
    public string QrImageUrl { get; set; } = string.Empty;
    public string QuickLinkUrl { get; set; } = string.Empty;
    public string BankBin { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string TransferContent { get; set; } = string.Empty;
}
