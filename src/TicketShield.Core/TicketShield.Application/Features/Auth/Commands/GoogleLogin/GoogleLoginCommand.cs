using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Auth.Models;

namespace TicketShield.Application.Features.Auth.Commands.GoogleLogin;

public class GoogleLoginCommand : IRequest<ApiResponse<AuthResponse>>
{
    public string IdToken { get; set; } = string.Empty;
}
