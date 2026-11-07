using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Disputes;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Disputes.Queries.GetDisputes;

public class GetDisputesQueryHandler : IRequestHandler<GetDisputesQuery, ApiResponse<DisputesOverviewDto>>
{
    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ITicketShieldDbContext _db;
    private readonly ICurrentUserService? _currentUser;

    public GetDisputesQueryHandler(ITicketShieldDbContext db, ICurrentUserService? currentUser = null)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<DisputesOverviewDto>> Handle(GetDisputesQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser is not { IsAuthenticated: true, UserId: { } userId } || userId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để xem danh sách khiếu nại.");
        }

        if (_currentUser.Role is not ("Admin" or "Cskh" or "Moderator"))
        {
            throw new ForbiddenAccessException("Bạn không có quyền xem danh sách khiếu nại.");
        }

        var baseQuery = _db.Disputes
            .AsNoTracking()
            .Include(d => d.Escrow)
                .ThenInclude(e => e.Listing)
                    .ThenInclude(l => l.Event)
            .Include(d => d.Escrow)
                .ThenInclude(e => e.Seller)
            .Include(d => d.Buyer)
            .Include(d => d.Evidences)
            .AsQueryable();

        // Apply search keyword first to both count queries and items query
        if (!string.IsNullOrWhiteSpace(request.SearchKeyword))
        {
            var kw = request.SearchKeyword.Trim().ToLower();
            baseQuery = baseQuery.Where(d =>
                d.DisputeCode.ToLower().Contains(kw) ||
                d.Description.ToLower().Contains(kw) ||
                (d.Escrow.Listing != null && d.Escrow.Listing.Event != null && d.Escrow.Listing.Event.Name.ToLower().Contains(kw)) ||
                (d.Escrow.Listing != null && d.Escrow.Listing.OriginalTicketCode.ToLower().Contains(kw)) ||
                (d.Escrow.NewTicketCode != null && d.Escrow.NewTicketCode.ToLower().Contains(kw)) ||
                d.Buyer.FullName.ToLower().Contains(kw) ||
                d.Escrow.Seller.FullName.ToLower().Contains(kw)
            );
        }

        // Compute counts before status filtering
        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var openCount = await baseQuery.CountAsync(d => d.Status == DisputeStatus.Open, cancellationToken);
        var resolvedCount = await baseQuery.CountAsync(d => d.Status == DisputeStatus.Resolved, cancellationToken);
        var rejectedCount = await baseQuery.CountAsync(d => d.Status == DisputeStatus.Rejected, cancellationToken);

        // Apply status filter for paginated list items
        var itemsQuery = baseQuery;
        if (!string.IsNullOrWhiteSpace(request.Status) && request.Status.ToLower() != "all")
        {
            if (Enum.TryParse<DisputeStatus>(request.Status, true, out var statusEnum))
            {
                itemsQuery = itemsQuery.Where(d => d.Status == statusEnum);
            }
        }

        var list = await itemsQuery
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        var items = list.Select(d =>
        {
            var scans = string.IsNullOrWhiteSpace(d.GateLogSnapshot)
                ? new List<GateScan>()
                : JsonSerializer.Deserialize<List<GateScan>>(d.GateLogSnapshot, SnapshotJson) ?? new List<GateScan>();

            return new DisputeListItemDto
            {
                DisputeId = d.Id,
                DisputeCode = d.DisputeCode,
                ReasonCode = d.ReasonCode,
                Description = d.Description,
                Status = d.Status.ToString(),
                Resolution = d.Resolution?.ToString(),
                RefundAmount = d.RefundAmount,
                AdminNotes = d.AdminNotes,
                Recommendation = d.Recommendation,
                CreatedAt = d.CreatedAt,
                ResolvedAt = d.ResolvedAt,
                TransferredAt = d.Escrow.TransferredAt,
                HarvestedAt = d.HarvestedAt,
                EscrowId = d.EscrowId,
                TotalBuyerPaid = d.Escrow.TotalBuyerPaid,
                EventTitle = d.Escrow.Listing?.Event?.Name ?? "Sự kiện",
                TicketCode = d.Escrow.NewTicketCode ?? d.Escrow.Listing?.OriginalTicketCode ?? "N/A",
                BuyerId = d.BuyerId,
                BuyerName = d.Buyer.FullName,
                BuyerEmail = d.Buyer.Email,
                SellerId = d.Escrow.SellerId,
                SellerName = d.Escrow.Seller.FullName,
                SellerEmail = d.Escrow.Seller.Email,
                Evidences = d.Evidences.Select(e => new DisputeEvidenceDto
                {
                    EvidenceId = e.Id,
                    // file_url có thể là secure_url Cloudinary mở được mãi; FE lấy ảnh qua API evidence.
                    FileUrl = string.Empty,
                    CreatedAt = e.CreatedAt
                }).ToList(),
                Scans = scans
            };
        }).ToList();

        return ApiResponse<DisputesOverviewDto>.SuccessResponse(new DisputesOverviewDto
        {
            Items = items,
            TotalCount = totalCount,
            OpenCount = openCount,
            ResolvedCount = resolvedCount,
            RejectedCount = rejectedCount
        });
    }
}
