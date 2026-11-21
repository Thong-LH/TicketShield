using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Disputes;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Disputes.Queries.GetDisputeRecommendation;

public class GetDisputeRecommendationQueryHandler : IRequestHandler<GetDisputeRecommendationQuery, ApiResponse<DisputeRecommendationResponse>>
{
    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ITicketShieldDbContext _db;
    private readonly ICurrentUserService? _currentUser;

    public GetDisputeRecommendationQueryHandler(ITicketShieldDbContext db, ICurrentUserService? currentUser = null)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<DisputeRecommendationResponse>> Handle(GetDisputeRecommendationQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser is not { IsAuthenticated: true, UserId: { } userId } || userId == Guid.Empty)
        {
            throw new UnauthorizedException("You must be logged in to view dispute dossier.");
        }

        if (_currentUser.Role is not ("Admin" or "Cskh" or "Moderator"))
        {
            throw new ForbiddenAccessException("You do not have permission to view this dispute dossier.");
        }

        var dispute = await _db.Disputes
            .AsNoTracking()
            .Include(row => row.Escrow)
            .FirstOrDefaultAsync(row => row.Id == request.DisputeId, cancellationToken);
        if (dispute == null)
        {
            throw new NotFoundException("Dispute", request.DisputeId);
        }

        var scans = string.IsNullOrWhiteSpace(dispute.GateLogSnapshot)
            ? new List<GateScan>()
            : JsonSerializer.Deserialize<List<GateScan>>(dispute.GateLogSnapshot, SnapshotJson) ?? new List<GateScan>();

        return ApiResponse<DisputeRecommendationResponse>.SuccessResponse(new DisputeRecommendationResponse
        {
            Recommendation = dispute.Recommendation,
            TransferredAt = dispute.Escrow.TransferredAt,
            HarvestedAt = dispute.HarvestedAt,
            Scans = scans
        });
    }
}
