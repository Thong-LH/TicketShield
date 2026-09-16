using FluentValidation;

namespace TicketShield.Application.Features.Admin.EventMarkup.Commands.UpdateEventResaleMarkup;

public class UpdateEventResaleMarkupCommandValidator : AbstractValidator<UpdateEventResaleMarkupCommand>
{
    public UpdateEventResaleMarkupCommandValidator()
    {
        RuleFor(x => x.EventId)
            .NotEmpty();

        RuleFor(x => x.MaxResaleMarkupPercentage)
            .InclusiveBetween(0m, 100m)
            .WithMessage("Biên độ trần giá (maxResaleMarkupPercentage) phải nằm trong khoảng 0% đến 100%.");
    }
}
