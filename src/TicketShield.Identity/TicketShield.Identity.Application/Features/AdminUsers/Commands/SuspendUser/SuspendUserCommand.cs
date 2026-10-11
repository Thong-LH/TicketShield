using MediatR;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.AdminUsers.Models;

namespace TicketShield.Identity.Application.Features.AdminUsers.Commands.SuspendUser;

public record SuspendUserCommand(Guid UserId) : IRequest<ApiResponse<AdminUserDto>>;
