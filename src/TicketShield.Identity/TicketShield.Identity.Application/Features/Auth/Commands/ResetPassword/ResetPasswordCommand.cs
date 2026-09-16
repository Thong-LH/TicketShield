using MediatR;
using TicketShield.Identity.Application.Common.Models;

namespace TicketShield.Identity.Application.Features.Auth.Commands.ResetPassword;

public record ResetPasswordCommand(
    string Email,
    string Otp,
    string NewPassword
) : IRequest<ApiResponse<string>>;
