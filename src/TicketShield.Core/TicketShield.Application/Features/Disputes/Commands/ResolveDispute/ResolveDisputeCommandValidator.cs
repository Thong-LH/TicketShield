using FluentValidation;

namespace TicketShield.Application.Features.Disputes.Commands.ResolveDispute;

public class ResolveDisputeCommandValidator : AbstractValidator<ResolveDisputeCommand>
{
    public ResolveDisputeCommandValidator()
    {
        RuleFor(v => v.Decision)
            .NotNull()
            .IsInEnum()
            .WithMessage("Invalid decision.");

        RuleFor(v => v.Reason)
            .NotEmpty()
            .WithMessage("Resolution reason must not be empty.");
    }
}
