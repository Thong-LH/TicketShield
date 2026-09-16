using MediatR;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.Auth.Models;

namespace TicketShield.Identity.Application.Features.Auth.Queries.GetCurrentUser;

public record GetCurrentUserQuery : IRequest<ApiResponse<UserProfileDto>>;
