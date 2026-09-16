using MediatR;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.Auth.Models;

namespace TicketShield.Identity.Application.Features.Auth.Commands.GoogleLogin;

public record GoogleLoginCommand(
    string IdToken
) : IRequest<ApiResponse<AuthResponse>>;
