using MediatR;
using TicketShield.Identity.Application.Common.Models;

namespace TicketShield.Identity.Application.Features.Auth.Commands.ForgotPassword;

public record ForgotPasswordCommand(
    string Email
) : IRequest<ApiResponse<string>>;
