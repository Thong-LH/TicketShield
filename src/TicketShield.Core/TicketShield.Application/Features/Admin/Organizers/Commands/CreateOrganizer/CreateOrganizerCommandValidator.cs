using FluentValidation;

namespace TicketShield.Application.Features.Admin.Organizers.Commands.CreateOrganizer;

public class CreateOrganizerCommandValidator : AbstractValidator<CreateOrganizerCommand>
{
    public CreateOrganizerCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Organizer name must not be empty.")
            .MaximumLength(200).WithMessage("Organizer name must not exceed 200 characters.");

        RuleFor(x => x.OfficialEmail)
            .NotEmpty().WithMessage("Official email must not be empty.")
            .EmailAddress().WithMessage("Invalid email format.");

        When(x => !string.IsNullOrEmpty(x.ApiBaseUrl), () =>
        {
            RuleFor(x => x.ApiBaseUrl)
                .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
                .WithMessage("API Base URL must be a valid absolute URL.");
        });

        When(x => !string.IsNullOrEmpty(x.GrpcEndpoint), () =>
        {
            RuleFor(x => x.GrpcEndpoint)
                .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
                .WithMessage("gRPC Endpoint must be a valid absolute URL.");
        });
    }
}
