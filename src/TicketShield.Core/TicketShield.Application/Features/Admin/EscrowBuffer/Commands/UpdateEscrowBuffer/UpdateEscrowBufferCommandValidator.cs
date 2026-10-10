using FluentValidation;

namespace TicketShield.Application.Features.Admin.EscrowBuffer.Commands.UpdateEscrowBuffer;

public class UpdateEscrowBufferCommandValidator : AbstractValidator<UpdateEscrowBufferCommand>
{
    public const int MinSeconds = 1;
    public const int MaxSeconds = 2_592_000;

    public UpdateEscrowBufferCommandValidator()
    {
        RuleFor(x => x.BufferSeconds)
            .InclusiveBetween(MinSeconds, MaxSeconds)
            .WithMessage("bufferSeconds phải là số nguyên từ 1 đến 2592000.");

        RuleFor(x => x.CutoffSeconds)
            .InclusiveBetween(MinSeconds, MaxSeconds)
            .WithMessage("cutoffSeconds phải là số nguyên từ 1 đến 2592000.");
    }
}
