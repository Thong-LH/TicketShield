namespace TicketShield.Application.Features.Admin.FeeSettings.Models;

public class ResaleFeeCalculationResult
{
    public decimal OriginalPrice { get; set; }
    public decimal BuyerFee { get; set; }
    public decimal SellerFee { get; set; }
    public decimal TotalBuyerPaid { get; set; }
    public decimal NetSellerPayout { get; set; }
}
