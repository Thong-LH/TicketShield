using FluentValidation;

namespace TicketShield.Application.Features.Admin.FeeSettings.Commands.UpdateFeeSettings;

public class UpdateFeeSettingsCommandValidator : AbstractValidator<UpdateFeeSettingsCommand>
{
    public UpdateFeeSettingsCommandValidator()
    {
        RuleFor(x => x.BuyerFeePercentage)
            .InclusiveBetween(0.00m, 0.15m)
            .WithMessage("Phí người mua (buyerFeePercentage) phải nằm trong khoảng từ 0% (0.00) đến 15% (0.15).");

        RuleFor(x => x.SellerFeePercentage)
            .InclusiveBetween(0.00m, 0.10m)
            .WithMessage("Phí người bán (sellerFeePercentage) phải nằm trong khoảng từ 0% (0.00) đến 10% (0.10).");

        RuleFor(x => x.MinimumBuyerFee)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("Phí tối thiểu người mua (minimumBuyerFee) phải lớn hơn hoặc bằng 0.");

        RuleFor(x => x.MinimumSellerFee)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("Phí tối thiểu người bán (minimumSellerFee) phải lớn hơn hoặc bằng 0.");
    }
}
