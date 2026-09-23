using FluentValidation;

namespace TicketShield.Identity.Application.Features.Auth.Commands.UpdateProfile;

public class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(v => v.FullName)
            .NotEmpty().WithMessage("Họ và tên không được để trống.")
            .MaximumLength(100).WithMessage("Họ và tên không được vượt quá 100 ký tự.");

        RuleFor(v => v.PhoneNumber)
            .MaximumLength(20).WithMessage("Số điện thoại không được vượt quá 20 ký tự.")
            .Matches(@"^[0-9+\s\-()]*$").When(v => !string.IsNullOrEmpty(v.PhoneNumber))
            .WithMessage("Số điện thoại không đúng định dạng.");

        RuleFor(v => v.IdCardNumber)
            .MaximumLength(20).WithMessage("Số CCCD/CMND không được vượt quá 20 ký tự.");
    }
}
