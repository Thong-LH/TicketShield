using FluentValidation;

namespace TicketShield.Application.Features.Disputes.Commands.CreateDispute;

public class CreateDisputeCommandValidator : AbstractValidator<CreateDisputeCommand>
{
    public CreateDisputeCommandValidator()
    {
        RuleFor(v => v.Reason)
            .NotEmpty()
            .WithMessage("Lý do khiếu nại không được để trống.");
    }
}
