using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Auth.Models;

namespace TicketShield.Application.Features.Auth.Commands.Login;

public class LoginCommand : IRequest<ApiResponse<AuthResponse>>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
