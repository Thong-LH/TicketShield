using FluentValidation;

namespace TicketShield.Identity.Application.Features.Auth.Commands.GoogleLogin;

public class GoogleLoginCommandValidator : AbstractValidator<GoogleLoginCommand>
{
    public GoogleLoginCommandValidator()
    {
        RuleFor(v => v.IdToken)
            .NotEmpty().WithMessage("Google IdToken không được để trống.");
    }
}
