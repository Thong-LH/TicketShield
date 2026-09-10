using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Auth.Commands.ResetPassword;

public class ResetPasswordCommand : IRequest<ApiResponse<string>>
{
    public string Email { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
