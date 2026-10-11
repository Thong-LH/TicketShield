using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Disputes.Queries.GetDisputes;

public class GetDisputesQuery : IRequest<ApiResponse<DisputesOverviewDto>>
{
    public string? Status { get; set; }
    public string? SearchKeyword { get; set; }
}
