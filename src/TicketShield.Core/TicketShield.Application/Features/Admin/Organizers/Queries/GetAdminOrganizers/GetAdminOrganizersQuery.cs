using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Organizers.Models;

namespace TicketShield.Application.Features.Admin.Organizers.Queries.GetAdminOrganizers;

public record GetAdminOrganizersQuery : IRequest<ApiResponse<List<AdminOrganizerDto>>>;
