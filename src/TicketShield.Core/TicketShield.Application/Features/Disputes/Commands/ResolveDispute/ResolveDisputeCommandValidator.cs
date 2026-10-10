using FluentValidation;

namespace TicketShield.Application.Features.Disputes.Commands.ResolveDispute;

public class ResolveDisputeCommandValidator : AbstractValidator<ResolveDisputeCommand>
{
    public ResolveDisputeCommandValidator()
    {
        RuleFor(v => v.Decision)
            .NotNull()
            .IsInEnum()
            .WithMessage("Quyết định không hợp lệ.");

        RuleFor(v => v.Reason)
            .NotEmpty()
            .WithMessage("Lý do xử lý không được để trống.");
    }
}
