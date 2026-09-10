using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Auth.Queries.GetCurrentUser;

public class GetCurrentUserQuery : IRequest<ApiResponse<UserProfileDto>>
{
}
