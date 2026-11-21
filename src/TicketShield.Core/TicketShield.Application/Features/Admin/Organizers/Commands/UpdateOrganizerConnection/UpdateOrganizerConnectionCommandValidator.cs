using FluentValidation;

namespace TicketShield.Application.Features.Admin.Organizers.Commands.UpdateOrganizerConnection;

public class UpdateOrganizerConnectionCommandValidator : AbstractValidator<UpdateOrganizerConnectionCommand>
{
    public UpdateOrganizerConnectionCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Organizer name must not be empty.")
            .MaximumLength(200).WithMessage("Organizer name must not exceed 200 characters.");

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

        RuleFor(x => x.Status)
            .Must(s => s is "ACTIVE" or "INACTIVE")
            .WithMessage("Status must be 'ACTIVE' or 'INACTIVE'.");

        RuleFor(x => x.ConnectionStatus)
            .Must(s => s is "CONNECTED" or "DISCONNECTED" or "PENDING")
            .WithMessage("Connection status must be 'CONNECTED', 'DISCONNECTED', or 'PENDING'.");
    }
}
