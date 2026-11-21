using FluentValidation;

namespace TicketShield.Application.Features.Admin.FeeSettings.Commands.UpdateFeeSettings;

public class UpdateFeeSettingsCommandValidator : AbstractValidator<UpdateFeeSettingsCommand>
{
    public UpdateFeeSettingsCommandValidator()
    {
        RuleFor(x => x.BuyerFeePercentage)
            .InclusiveBetween(0.00m, 0.15m)
            .WithMessage("Buyer fee percentage (buyerFeePercentage) must be between 0% (0.00) and 15% (0.15).");

        RuleFor(x => x.SellerFeePercentage)
            .InclusiveBetween(0.00m, 0.10m)
            .WithMessage("Seller fee percentage (sellerFeePercentage) must be between 0% (0.00) and 10% (0.10).");

        RuleFor(x => x.MinimumBuyerFee)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("Minimum buyer fee (minimumBuyerFee) must be greater than or equal to 0.");

        RuleFor(x => x.MinimumSellerFee)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("Minimum seller fee (minimumSellerFee) must be greater than or equal to 0.");
    }
}
