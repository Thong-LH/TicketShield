using MediatR;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.AdminUsers.Models;

namespace TicketShield.Identity.Application.Features.AdminUsers.Queries.GetAdminUsersList;

public record GetAdminUsersListQuery : IRequest<ApiResponse<List<AdminUserDto>>>;
