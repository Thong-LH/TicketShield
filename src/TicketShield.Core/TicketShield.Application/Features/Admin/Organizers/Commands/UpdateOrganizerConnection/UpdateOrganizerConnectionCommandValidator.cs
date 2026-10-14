using FluentValidation;

namespace TicketShield.Application.Features.Admin.Organizers.Commands.UpdateOrganizerConnection;

public class UpdateOrganizerConnectionCommandValidator : AbstractValidator<UpdateOrganizerConnectionCommand>
{
    public UpdateOrganizerConnectionCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên ban tổ chức không được để trống.")
            .MaximumLength(200).WithMessage("Tên ban tổ chức không được vượt quá 200 ký tự.");

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

        RuleFor(x => x.Status)
            .Must(s => s is "ACTIVE" or "INACTIVE")
            .WithMessage("Trạng thái phải là 'ACTIVE' hoặc 'INACTIVE'.");

        RuleFor(x => x.ConnectionStatus)
            .Must(s => s is "CONNECTED" or "DISCONNECTED" or "PENDING")
            .WithMessage("Trạng thái kết nối phải là 'CONNECTED', 'DISCONNECTED' hoặc 'PENDING'.");
    }
}
