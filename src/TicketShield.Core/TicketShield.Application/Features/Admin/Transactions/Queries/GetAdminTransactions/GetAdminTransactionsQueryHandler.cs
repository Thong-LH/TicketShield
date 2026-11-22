using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Helpers;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Transactions.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Admin.Transactions.Queries.GetAdminTransactions;

/// <summary>
/// Handler xử lý truy vấn danh sách giao dịch Ký quỹ Escrow và Sổ cái giải ngân Payout Ledger cho Admin
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-334 (BE-11.1.1)
/// </summary>
public class GetAdminTransactionsQueryHandler : IRequestHandler<GetAdminTransactionsQuery, ApiResponse<AdminTransactionsResponseDto>>
{
    private readonly ITicketShieldDbContext _dbContext;

    public GetAdminTransactionsQueryHandler(ITicketShieldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<AdminTransactionsResponseDto>> Handle(GetAdminTransactionsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page > 0 ? request.Page : 1;
        var size = request.Size > 0 ? request.Size : 20;

        // 1. Every filter the table applies. The summary cards and the table read the same rows (SCRUM-394).
        var query = ApplyFilters(_dbContext.EscrowTransactions.AsNoTracking(), request);

        // 2. Summary cards over the filtered rows, across all pages.
        //    Money buckets are shared with the Admin Dashboard so Inflow - Paid out - Refunded - Held = Net revenue.
        var totalInflowAmount = await query
            .Where(EscrowMoneyBuckets.IsInflow)
            .SumAsync(e => (decimal?)e.TotalBuyerPaid, cancellationToken) ?? 0m;

        // Net revenue: fees of the Released escrows, same formula as the Admin Dashboard.
        // Summed from the fees rather than subtracted from the other cards, so a Released escrow
        // without a Success payout does not add the seller's money to it.
        var totalNetRevenue = await query
            .Where(e => e.Status == EscrowStatus.Released)
            .SumAsync(e => (decimal?)(e.BuyerFee + e.SellerFee), cancellationToken) ?? 0m;

        var heldQuery = query.Where(EscrowMoneyBuckets.IsHeld);
        var totalEscrowLockedAmount = await heldQuery
            .SumAsync(e => (decimal?)e.TotalBuyerPaid, cancellationToken) ?? 0m;
        var totalLockedCount = await heldQuery.CountAsync(cancellationToken);

        var totalPayoutCompletedAmount = await query
            .Where(EscrowMoneyBuckets.HasSuccessfulPayout)
            .SumAsync(e => (decimal?)e.PayoutTransaction!.Amount, cancellationToken) ?? 0m;

        var totalRefundedAmount = await query
            .Where(e => e.Status == EscrowStatus.Refunded)
            .SumAsync(e => (decimal?)e.TotalBuyerPaid, cancellationToken) ?? 0m;

        var totalDisputedCount = await query
            .CountAsync(e => e.Status == EscrowStatus.Disputed, cancellationToken);

        var totalCount = await query.CountAsync(cancellationToken);

        // 3. Load the requested page with everything the table and the detail drawer show, newest first.
        var items = await query
            .Include(e => e.Listing)
                .ThenInclude(l => l.Event)
            .Include(e => e.Buyer)
            .Include(e => e.Seller)
            .Include(e => e.PayoutTransaction)
            .Include(e => e.Dispute)
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        // 4. Map DTO
        var dtos = items.Select(e => new AdminTransactionDto
        {
            EscrowId = e.Id,
            ListingId = e.ListingId,
            BundleId = e.BundleId,
            EventId = e.Listing?.EventId ?? Guid.Empty,
            EventName = e.Listing?.Event?.Name ?? string.Empty,
            OriginalTicketCode = e.Listing?.OriginalTicketCode ?? string.Empty,
            NewTicketCode = e.NewTicketCode,
            BuyerId = e.BuyerId,
            BuyerName = e.Buyer?.FullName ?? string.Empty,
            BuyerEmail = e.Buyer?.Email ?? string.Empty,
            BuyerPhone = e.Buyer?.PhoneNumber,
            SellerId = e.SellerId,
            SellerName = e.Seller?.FullName ?? string.Empty,
            SellerEmail = e.Seller?.Email ?? string.Empty,
            SellerPhone = e.Seller?.PhoneNumber,
            OriginalTicketPrice = e.OriginalTicketPrice,
            BuyerFee = e.BuyerFee,
            SellerFee = e.SellerFee,
            TotalBuyerPaid = e.TotalBuyerPaid,
            NetSellerPayout = e.NetSellerPayout,
            PaymentReference = e.PaymentReference,
            BankTransactionReference = e.BankTransactionReference,
            Status = e.Status,
            InSettlementBuffer = e.InSettlementBuffer,
            TransferredAt = e.TransferredAt,
            UnlockAt = e.UnlockAt,
            DisputeDeadline = e.DisputeDeadline,
            DisputeWaived = e.DisputeWaived,
            DisputeWaivedAt = e.DisputeWaivedAt,
            RefundedAt = e.Status == EscrowStatus.Refunded ? e.UpdatedAt : null,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            PayoutId = e.PayoutTransaction?.Id,
            PayoutCode = e.PayoutTransaction?.PayoutCode,
            PayoutStatus = e.PayoutTransaction?.Status,
            PayoutAmount = e.PayoutTransaction?.Amount,
            RecipientBankCode = e.PayoutTransaction?.RecipientBankCode,
            RecipientAccountNumber = e.PayoutTransaction?.RecipientAccountNumber,
            RecipientAccountName = e.PayoutTransaction?.RecipientAccountName,
            BankReferenceCode = e.PayoutTransaction?.BankReferenceCode,
            PayoutProcessedAt = e.PayoutTransaction?.ProcessedAt,
            PayoutErrorMessage = e.PayoutTransaction?.LastErrorMessage,
            PayoutRetryCount = e.PayoutTransaction?.RetryCount ?? 0,
            DisputeId = e.Dispute?.Id,
            DisputeStatus = e.Dispute?.Status
        }).ToList();

        var paginatedList = new PaginatedList<AdminTransactionDto>(dtos, totalCount, page, size);

        var result = new AdminTransactionsResponseDto
        {
            Transactions = paginatedList,
            Summary = new AdminTransactionsSummaryDto
            {
                TotalInflowAmount = totalInflowAmount,
                TotalNetRevenue = totalNetRevenue,
                TotalEscrowLockedAmount = totalEscrowLockedAmount,
                TotalLockedCount = totalLockedCount,
                TotalPayoutCompletedAmount = totalPayoutCompletedAmount,
                TotalRefundedAmount = totalRefundedAmount,
                TotalDisputedCount = totalDisputedCount
            }
        };

        return ApiResponse<AdminTransactionsResponseDto>.SuccessResponse(result, "Admin escrow transactions and payout ledger retrieved successfully.");
    }

    private static IQueryable<EscrowTransaction> ApplyFilters(IQueryable<EscrowTransaction> query, GetAdminTransactionsQuery request)
    {
        // Lọc theo trạng thái Escrow
        if (request.Status.HasValue)
        {
            query = query.Where(e => e.Status == request.Status.Value);
        }

        // Lọc theo trạng thái giải ngân Payout
        if (request.PayoutStatus.HasValue)
        {
            query = query.Where(e => e.PayoutTransaction != null && e.PayoutTransaction.Status == request.PayoutStatus.Value);
        }

        // Lọc theo cờ đệm an toàn (Settlement Buffer)
        if (request.InSettlementBufferOnly.HasValue && request.InSettlementBufferOnly.Value)
        {
            query = query.Where(e => e.InSettlementBuffer);
        }

        // Lọc theo khoảng ngày tạo (chuẩn hóa về UTC phòng thủ múi giờ)
        if (request.FromDate.HasValue)
        {
            var fromUtc = request.FromDate.Value.ToUniversalTime();
            query = query.Where(e => e.CreatedAt >= fromUtc);
        }

        if (request.ToDate.HasValue)
        {
            var toUtc = request.ToDate.Value.ToUniversalTime();
            query = query.Where(e => e.CreatedAt <= toUtc);
        }

        // Tìm kiếm từ khóa
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            query = query.Where(e =>
                (e.PaymentReference != null && e.PaymentReference.ToLower().Contains(kw)) ||
                (e.BankTransactionReference != null && e.BankTransactionReference.ToLower().Contains(kw)) ||
                (e.NewTicketCode != null && e.NewTicketCode.ToLower().Contains(kw)) ||
                (e.Listing != null && e.Listing.OriginalTicketCode.ToLower().Contains(kw)) ||
                (e.Listing != null && e.Listing.Event != null && e.Listing.Event.Name.ToLower().Contains(kw)) ||
                (e.Buyer != null && (e.Buyer.FullName.ToLower().Contains(kw) || e.Buyer.Email.ToLower().Contains(kw))) ||
                (e.Seller != null && (e.Seller.FullName.ToLower().Contains(kw) || e.Seller.Email.ToLower().Contains(kw))) ||
                (e.PayoutTransaction != null && e.PayoutTransaction.PayoutCode.ToLower().Contains(kw)));
        }

        return query;
    }
}
