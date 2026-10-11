using MediatR;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.AdminUsers.Models;

namespace TicketShield.Identity.Application.Features.AdminUsers.Queries.GetAdminUser;

public record GetAdminUserQuery(Guid UserId) : IRequest<ApiResponse<AdminUserDto>>;
