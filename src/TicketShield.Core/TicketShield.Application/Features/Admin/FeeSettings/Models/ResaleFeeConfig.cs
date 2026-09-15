namespace TicketShield.Application.Features.Admin.FeeSettings.Models;

public class ResaleFeeConfig
{
    public decimal BuyerFeePercentage { get; set; } = 0.05m;
    public decimal SellerFeePercentage { get; set; } = 0.03m;
    public decimal MinimumBuyerFee { get; set; } = 10000m;
    public decimal MinimumSellerFee { get; set; } = 5000m;
}
