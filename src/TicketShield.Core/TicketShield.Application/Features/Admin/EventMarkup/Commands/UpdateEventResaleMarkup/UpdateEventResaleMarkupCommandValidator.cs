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
            .WithMessage("Max resale markup percentage (maxResaleMarkupPercentage) must be between 0% and 100%.");
    }
}
