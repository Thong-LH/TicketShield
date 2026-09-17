using FluentValidation;

namespace TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;

public class HoldListingForPurchaseCommandValidator : AbstractValidator<HoldListingForPurchaseCommand>
{
    public HoldListingForPurchaseCommandValidator()
    {
        RuleFor(v => v.ListingId)
            .NotEmpty()
            .WithMessage("Mã bài đăng bán vé (ListingId) không được để trống.");
    }
}
