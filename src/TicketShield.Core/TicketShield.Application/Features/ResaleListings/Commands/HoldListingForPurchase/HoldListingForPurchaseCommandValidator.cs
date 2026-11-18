using FluentValidation;

namespace TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;

public class HoldListingForPurchaseCommandValidator : AbstractValidator<HoldListingForPurchaseCommand>
{
    public HoldListingForPurchaseCommandValidator()
    {
        RuleFor(v => v.ListingId)
            .NotEmpty()
            .WithMessage("Mã bài đăng bán vé (ListingId) không được để trống.");

        RuleFor(v => v.SelectedListingIds)
            .Must(ids => ids == null || (ids.Count > 0 && ids.Distinct().Count() == ids.Count))
            .WithMessage("Danh sách vé chọn không được rỗng hoặc chứa mã trùng lặp.");
    }
}
