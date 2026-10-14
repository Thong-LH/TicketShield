using FluentValidation;

namespace TicketShield.Application.Features.Admin.Organizers.Commands.CreateOrganizer;

public class CreateOrganizerCommandValidator : AbstractValidator<CreateOrganizerCommand>
{
    public CreateOrganizerCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên ban tổ chức không được để trống.")
            .MaximumLength(200).WithMessage("Tên ban tổ chức không được vượt quá 200 ký tự.");

        RuleFor(x => x.OfficialEmail)
            .NotEmpty().WithMessage("Email chính thức không được để trống.")
            .EmailAddress().WithMessage("Email không đúng định dạng.");

        When(x => !string.IsNullOrEmpty(x.ApiBaseUrl), () =>
        {
            RuleFor(x => x.ApiBaseUrl)
                .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
                .WithMessage("API Base URL phải là một URL tuyệt đối hợp lệ.");
        });

        When(x => !string.IsNullOrEmpty(x.GrpcEndpoint), () =>
        {
            RuleFor(x => x.GrpcEndpoint)
                .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
                .WithMessage("gRPC Endpoint phải là một URL tuyệt đối hợp lệ.");
        });
    }
}
