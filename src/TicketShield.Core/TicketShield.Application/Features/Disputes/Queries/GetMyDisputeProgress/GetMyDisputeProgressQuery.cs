using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Disputes.Queries.GetMyDisputeProgress;

public class GetMyDisputeProgressQuery : IRequest<ApiResponse<MyDisputeProgressDto>>
{
    public Guid DisputeId { get; set; }
}
