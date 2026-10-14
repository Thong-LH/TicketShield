using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Organizers.Models;

namespace TicketShield.Application.Features.Admin.Organizers.Queries.GetAdminOrganizerDetail;

public record GetAdminOrganizerDetailQuery(Guid Id) : IRequest<ApiResponse<AdminOrganizerDto>>;
