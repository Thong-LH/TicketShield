using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Analytics.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Admin.Analytics.Queries.GetAdminPlatformAnalytics;

/// <summary>
/// Handler xử lý thống kê tổng doanh thu thực tế, người dùng (Buyer vs Seller), kho vé và giao dịch theo sự kiện
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-338 (BE-11.1.5)
/// </summary>
public class GetAdminPlatformAnalyticsQueryHandler : IRequestHandler<GetAdminPlatformAnalyticsQuery, ApiResponse<AdminPlatformAnalyticsResponseDto>>
{
    private readonly ITicketShieldDbContext _dbContext;

    public GetAdminPlatformAnalyticsQueryHandler(ITicketShieldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<AdminPlatformAnalyticsResponseDto>> Handle(GetAdminPlatformAnalyticsQuery request, CancellationToken cancellationToken)
    {
        // 1. Thống kê Doanh thu thực tế của sàn (Platform Revenue Stats)
        var escrowQuery = _dbContext.EscrowTransactions.AsNoTracking();
        if (request.FromDate.HasValue)
        {
            escrowQuery = escrowQuery.Where(e => e.CreatedAt >= request.FromDate.Value);
        }
        if (request.ToDate.HasValue)
        {
            escrowQuery = escrowQuery.Where(e => e.CreatedAt <= request.ToDate.Value);
        }

        var releasedEscrows = escrowQuery.Where(e => e.Status == EscrowStatus.Released);
        var totalBuyerFees = await releasedEscrows.SumAsync(e => (decimal?)e.BuyerFee, cancellationToken) ?? 0m;
        var totalSellerFees = await releasedEscrows.SumAsync(e => (decimal?)e.SellerFee, cancellationToken) ?? 0m;
        var totalGmv = await releasedEscrows.SumAsync(e => (decimal?)e.TotalBuyerPaid, cancellationToken) ?? 0m;
        var totalNetRevenue = totalBuyerFees + totalSellerFees;

        var lockedEscrows = escrowQuery.Where(e => e.Status == EscrowStatus.Locked);
        var pendingFeesInEscrow = (await lockedEscrows.SumAsync(e => (decimal?)(e.BuyerFee + e.SellerFee), cancellationToken)) ?? 0m;

        var totalRefundedAmount = await escrowQuery
            .Where(e => e.Status == EscrowStatus.Refunded)
            .SumAsync(e => (decimal?)e.TotalBuyerPaid, cancellationToken) ?? 0m;

        var revenueStats = new PlatformRevenueStatsDto
        {
            TotalNetRevenue = totalNetRevenue,
            TotalBuyerFees = totalBuyerFees,
            TotalSellerFees = totalSellerFees,
            TotalGmv = totalGmv,
            PendingFeesInEscrow = pendingFeesInEscrow,
            TotalRefundedAmount = totalRefundedAmount
        };

        // 2. Thống kê Người dùng (User Analytics: Buyer vs Seller)
        var totalUsers = await _dbContext.ShadowUsers.CountAsync(cancellationToken);
        var activeUsers = await _dbContext.ShadowUsers.CountAsync(u => u.IsActive, cancellationToken);

        var buyerIds = await escrowQuery
            .Select(e => e.BuyerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var listingQuery = _dbContext.ResaleListings.AsNoTracking();
        if (request.FromDate.HasValue)
        {
            listingQuery = listingQuery.Where(l => l.CreatedAt >= request.FromDate.Value);
        }
        if (request.ToDate.HasValue)
        {
            listingQuery = listingQuery.Where(l => l.CreatedAt <= request.ToDate.Value);
        }

        var sellerIds = await listingQuery
            .Select(l => l.SellerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var buyerSet = new HashSet<Guid>(buyerIds);
        var dualRoleUsers = sellerIds.Count(s => buyerSet.Contains(s));

        var userStats = new UserAnalyticsDto
        {
            TotalUsers = totalUsers,
            TotalBuyers = buyerIds.Count,
            TotalSellers = sellerIds.Count,
            DualRoleUsers = dualRoleUsers,
            ActiveUsers = activeUsers
        };

        // 3. Thống kê Kho vé Admin (Ticket Inventory Analytics)
        var totalTicketsInInventory = await _dbContext.Tickets.CountAsync(cancellationToken);
        var validTickets = await _dbContext.Tickets.CountAsync(t => t.Status == TicketStatus.Valid, cancellationToken);
        var lockedForResaleTickets = await _dbContext.Tickets.CountAsync(t => t.Status == TicketStatus.LockedForResale, cancellationToken);
        var transferredTickets = await _dbContext.Tickets.CountAsync(t => t.Status == TicketStatus.Transferred, cancellationToken);
        var usedTickets = await _dbContext.Tickets.CountAsync(t => t.Status == TicketStatus.Used, cancellationToken);
        var cancelledTickets = await _dbContext.Tickets.CountAsync(t => t.Status == TicketStatus.Cancelled, cancellationToken);

        var inventoryStats = new TicketInventoryAnalyticsDto
        {
            TotalTicketsInInventory = totalTicketsInInventory,
            ValidTickets = validTickets,
            LockedForResaleTickets = lockedForResaleTickets,
            TransferredTickets = transferredTickets,
            UsedTickets = usedTickets,
            CancelledTickets = cancelledTickets
        };

        // 4. Thống kê Số lượng vé giao dịch theo từng sự kiện (Event Transaction Analytics)
        var events = await _dbContext.Events
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var listingsWithEscrows = await listingQuery
            .Include(l => l.EscrowTransactions)
            .ToListAsync(cancellationToken);

        var listingsByEvent = listingsWithEscrows
            .GroupBy(l => l.EventId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var eventAnalyticsList = new List<EventTransactionAnalyticsDto>();
        foreach (var ev in events)
        {
            listingsByEvent.TryGetValue(ev.Id, out var listings);
            listings ??= new List<ResaleListing>();

            var totalEventListings = listings.Count;
            var totalSold = listings.Count(l => l.ListingStatus == ListingStatus.Sold);
            var totalAvailable = listings.Count(l => l.ListingStatus == ListingStatus.Verified);

            var successfulEscrows = listings
                .SelectMany(l => l.EscrowTransactions)
                .Where(es => es.Status == EscrowStatus.Released)
                .ToList();

            var gmv = successfulEscrows.Sum(es => es.TotalBuyerPaid);
            var revenue = successfulEscrows.Sum(es => es.BuyerFee + es.SellerFee);

            eventAnalyticsList.Add(new EventTransactionAnalyticsDto
            {
                EventId = ev.Id,
                EventName = ev.Name,
                Venue = ev.Venue,
                EventStartAt = ev.EventStartAt,
                TotalListings = totalEventListings,
                TotalTicketsSold = totalSold,
                TotalTicketsAvailable = totalAvailable,
                TotalTransactionVolume = gmv,
                TotalPlatformRevenue = revenue
            });
        }

        // Sắp xếp sự kiện theo doanh thu sàn cao nhất lên đầu, tiếp đến là số lượng vé đăng bán
        eventAnalyticsList = eventAnalyticsList
            .OrderByDescending(e => e.TotalPlatformRevenue)
            .ThenByDescending(e => e.TotalListings)
            .ToList();

        var response = new AdminPlatformAnalyticsResponseDto
        {
            Revenue = revenueStats,
            Users = userStats,
            Inventory = inventoryStats,
            EventTransactions = eventAnalyticsList
        };

        return ApiResponse<AdminPlatformAnalyticsResponseDto>.SuccessResponse(response, "Lấy dữ liệu thống kê doanh thu và kho vé thành công.");
    }
}

