using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Orders.Models;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Orders.Queries.GetBuyerOrderHistory;

/// <summary>
/// Handler xử lý lấy danh sách các giao dịch mua vé của User (Lịch sử đơn hàng & Sổ cái tài chính)
/// Đáp ứng yêu cầu SCRUM-340 / SCRUM-341 (BE-11.2.1)
/// </summary>
public class GetBuyerOrderHistoryQueryHandler : IRequestHandler<GetBuyerOrderHistoryQuery, ApiResponse<BuyerOrderHistoryResponseDto>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ICurrentUserService? _currentUserService;

    public GetBuyerOrderHistoryQueryHandler(ITicketShieldDbContext dbContext, ICurrentUserService? currentUserService = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<BuyerOrderHistoryResponseDto>> Handle(GetBuyerOrderHistoryQuery request, CancellationToken cancellationToken)
    {
        var buyerId = _currentUserService?.UserId ?? Guid.Empty;
        if (buyerId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để xem lịch sử đơn hàng.");
        }

        var page = request.Page > 0 ? request.Page : 1;
        var size = request.Size > 0 ? request.Size : 20;

        // 1. Thống kê tài chính cá nhân của Buyer
        var baseAllQuery = _dbContext.EscrowTransactions
            .AsNoTracking()
            .Where(e => e.BuyerId == buyerId);

        var totalOrders = await baseAllQuery.CountAsync(cancellationToken);

        var totalSpent = await baseAllQuery
            .Where(e => e.Status == EscrowStatus.Released || e.Status == EscrowStatus.Locked)
            .SumAsync(e => (decimal?)e.TotalBuyerPaid, cancellationToken) ?? 0m;

        var totalRefunded = await baseAllQuery
            .Where(e => e.Status == EscrowStatus.Refunded)
            .SumAsync(e => (decimal?)e.TotalBuyerPaid, cancellationToken) ?? 0m;

        var inEscrowOrdersCount = await baseAllQuery
            .CountAsync(e => e.Status == EscrowStatus.Locked, cancellationToken);

        // 2. Truy vấn danh sách đơn hàng có áp dụng bộ lọc
        var query = _dbContext.EscrowTransactions
            .AsNoTracking()
            .Include(e => e.Listing)
                .ThenInclude(l => l.Event)
            .Include(e => e.Listing)
                .ThenInclude(l => l.Tier)
            .Include(e => e.Dispute)
            .Where(e => e.BuyerId == buyerId);

        if (request.Status.HasValue)
        {
            query = query.Where(e => e.Status == request.Status.Value);
        }

        if (request.FromDate.HasValue)
        {
            query = query.Where(e => e.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(e => e.CreatedAt <= request.ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            query = query.Where(e =>
                (e.PaymentReference != null && e.PaymentReference.ToLower().Contains(kw)) ||
                (e.BankTransactionReference != null && e.BankTransactionReference.ToLower().Contains(kw)) ||
                (e.NewTicketCode != null && e.NewTicketCode.ToLower().Contains(kw)) ||
                (e.Listing != null && e.Listing.Event != null && e.Listing.Event.Name.ToLower().Contains(kw)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        // 3. Map sang DTO chi tiết
        var dtos = items.Select(e =>
        {
            var isRefunded = e.Status == EscrowStatus.Refunded;
            var isRefundQueued = e.Status == EscrowStatus.RefundQueued;
            var refundAmount = isRefunded
                ? (e.Dispute?.RefundAmount > 0 ? e.Dispute.RefundAmount : e.TotalBuyerPaid)
                : (isRefundQueued ? e.TotalBuyerPaid : 0m);

            var refundReason = e.Dispute != null
                ? $"Tranh chấp: {e.Dispute.ReasonCode} - {e.Dispute.Description}"
                : (isRefunded ? "Giao dịch đã được hoàn trả" : null);

            var orderCode = !string.IsNullOrWhiteSpace(e.PaymentReference)
                ? $"ORD-{e.PaymentReference}"
                : $"ORD-{e.Id.ToString("N")[..8].ToUpperInvariant()}";

            var statusDesc = e.Status switch
            {
                EscrowStatus.Pending => "Chờ thanh toán",
                EscrowStatus.Locked => "Đã thanh toán - Đang giữ ký quỹ an toàn",
                EscrowStatus.Releasing => "Đang tiến hành giải ngân",
                EscrowStatus.Released => "Giao dịch thành công - Đã sang tên vé",
                EscrowStatus.Disputed => "Đang trong quá trình khiếu nại",
                EscrowStatus.RefundQueued => "Đang xếp hàng chờ hoàn tiền",
                EscrowStatus.Refunded => "Đã hoàn tiền cho người mua",
                EscrowStatus.Cancelled => "Đã hủy",
                EscrowStatus.Expired => "Đã hết hạn",
                _ => e.Status.ToString()
            };

            return new BuyerOrderDto
            {
                OrderId = e.Id,
                OrderCode = orderCode,
                ListingId = e.ListingId,
                BundleId = e.BundleId,
                BundleTotalTickets = e.Listing?.BundleTotalTickets ?? (e.BundleId.HasValue ? 1 : 0),
                EventId = e.Listing?.EventId ?? Guid.Empty,
                EventName = e.Listing?.Event?.Name ?? string.Empty,
                EventVenue = e.Listing?.Event?.Venue ?? string.Empty,
                EventStartAt = e.Listing?.Event?.EventStartAt ?? DateTimeOffset.MinValue,
                TierName = e.Listing?.Tier?.TierName ?? string.Empty,
                SeatZone = e.Listing?.SeatZone,
                TicketCode = !string.IsNullOrWhiteSpace(e.NewTicketCode) ? e.NewTicketCode : e.Listing?.OriginalTicketCode,
                OriginalTicketPrice = e.OriginalTicketPrice,
                BuyerFee = e.BuyerFee,
                TotalBuyerPaid = e.TotalBuyerPaid,
                PaymentReference = e.PaymentReference,
                BankTransactionReference = e.BankTransactionReference,
                Status = e.Status,
                StatusDescription = statusDesc,
                TransferredAt = e.TransferredAt,
                DisputeDeadline = e.DisputeDeadline,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt,
                IsRefunded = isRefunded,
                IsRefundQueued = isRefundQueued,
                RefundAmount = refundAmount,
                RefundedAt = isRefunded ? e.UpdatedAt : null,
                RefundReason = refundReason,
                DisputeId = e.Dispute?.Id,
                DisputeStatus = e.Dispute?.Status,
                DisputeWaived = e.DisputeWaived
            };
        }).ToList();

        var paginatedList = new PaginatedList<BuyerOrderDto>(dtos, totalCount, page, size);

        var response = new BuyerOrderHistoryResponseDto
        {
            Orders = paginatedList,
            Summary = new BuyerOrderSummaryDto
            {
                TotalOrders = totalOrders,
                TotalSpent = totalSpent,
                TotalRefunded = totalRefunded,
                InEscrowOrdersCount = inEscrowOrdersCount
            }
        };

        return ApiResponse<BuyerOrderHistoryResponseDto>.SuccessResponse(response, "Lấy lịch sử đơn hàng của người mua thành công.");
    }
}

