using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Admin.FeeSettings.Commands.UpdateFeeSettings;

public class UpdateFeeSettingsCommand : IRequest<ApiResponse<string>>
{
    public decimal BuyerFeePercentage { get; set; }
    public decimal SellerFeePercentage { get; set; }
    public decimal MinimumBuyerFee { get; set; }
    public decimal MinimumSellerFee { get; set; }
}
