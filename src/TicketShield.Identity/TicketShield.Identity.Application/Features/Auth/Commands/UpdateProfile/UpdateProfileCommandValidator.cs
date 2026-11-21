using FluentValidation;

namespace TicketShield.Identity.Application.Features.Auth.Commands.UpdateProfile;

public class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(v => v.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(100).WithMessage("Full name cannot exceed 100 characters.");

        RuleFor(v => v.PhoneNumber)
            .MaximumLength(20).WithMessage("Phone number cannot exceed 20 characters.")
            .Matches(@"^[0-9+\s\-()]*$").When(v => !string.IsNullOrEmpty(v.PhoneNumber))
            .WithMessage("Invalid phone number format.");

        RuleFor(v => v.IdCardNumber)
            .MaximumLength(20).WithMessage("National ID/Citizen ID cannot exceed 20 characters.");
    }
}
