using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Organizers.Queries.GetOrganizers;

/// <summary>
/// Query lấy danh sách các Ban tổ chức hợp tác đang hoạt động (ACTIVE)
/// </summary>
public sealed record GetOrganizersQuery : IRequest<ApiResponse<List<OrganizerDto>>>;
