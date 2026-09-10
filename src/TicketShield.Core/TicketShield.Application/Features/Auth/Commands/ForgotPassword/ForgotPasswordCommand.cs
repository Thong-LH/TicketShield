using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Auth.Commands.ForgotPassword;

public class ForgotPasswordCommand : IRequest<ApiResponse<string>>
{
    public string Email { get; set; } = string.Empty;
}
