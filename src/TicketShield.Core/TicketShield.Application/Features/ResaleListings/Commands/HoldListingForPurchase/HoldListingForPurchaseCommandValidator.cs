using FluentValidation;

namespace TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;

public class HoldListingForPurchaseCommandValidator : AbstractValidator<HoldListingForPurchaseCommand>
{
    public HoldListingForPurchaseCommandValidator()
    {
        RuleFor(v => v.ListingId)
            .NotEmpty()
            .WithMessage("Listing ID (ListingId) is required.");

        RuleFor(v => v.SelectedListingIds)
            .Must(ids => ids == null || (ids.Count > 0 && ids.Distinct().Count() == ids.Count))
            .WithMessage("Selected tickets list cannot be empty or contain duplicate IDs.");
    }
}
