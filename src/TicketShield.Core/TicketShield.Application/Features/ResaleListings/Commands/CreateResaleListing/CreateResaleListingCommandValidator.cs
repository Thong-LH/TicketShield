using FluentValidation;

namespace TicketShield.Application.Features.ResaleListings.Commands.CreateResaleListing;

public class CreateResaleListingCommandValidator : AbstractValidator<CreateResaleListingCommand>
{
    public CreateResaleListingCommandValidator()
    {
        RuleFor(x => x.EventId)
            .NotEmpty().WithMessage("Mã sự kiện (EventId) không được để trống.");

        RuleFor(x => x.TierId)
            .NotEmpty().WithMessage("Mã hạng vé (TierId) không được để trống.");

        RuleFor(x => x.OriginalTicketCode)
            .NotEmpty().WithMessage("Mã vé gốc (OriginalTicketCode) không được để trống.")
            .MaximumLength(100).WithMessage("Mã vé gốc không được vượt quá 100 ký tự.");

        RuleFor(x => x.OriginalPrice)
            .GreaterThan(0).WithMessage("Giá gốc của vé phải lớn hơn 0.");

        RuleFor(x => x.ResalePrice)
            .GreaterThan(0).WithMessage("Giá bán lại phải lớn hơn 0.")
            .LessThanOrEqualTo(x => x.OriginalPrice)
            .WithMessage(x => $"Giá bán lại ({x.ResalePrice:N0} VNĐ) không được vượt quá giá gốc ({x.OriginalPrice:N0} VNĐ) theo Quy định chống đầu cơ của TicketShield.");
    }
}
