using MediatR;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.Auth.Models;

namespace TicketShield.Identity.Application.Features.Auth.Commands.Register;

public record RegisterCommand(
    string Email,
    string Password,
    string FullName,
    string? PhoneNumber = null,
    string? IdCardNumber = null
) : IRequest<ApiResponse<AuthResponse>>;
