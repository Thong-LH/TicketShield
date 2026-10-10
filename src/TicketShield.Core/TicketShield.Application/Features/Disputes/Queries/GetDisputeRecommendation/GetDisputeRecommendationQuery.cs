using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Disputes.Queries.GetDisputeRecommendation;

public class GetDisputeRecommendationQuery : IRequest<ApiResponse<DisputeRecommendationResponse>>
{
    public Guid DisputeId { get; set; }
}
