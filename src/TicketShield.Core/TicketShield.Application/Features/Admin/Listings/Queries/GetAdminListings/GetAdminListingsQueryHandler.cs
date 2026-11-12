using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Listings.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Admin.Listings.Queries.GetAdminListings;

/// <summary>
/// Handler xử lý MediatR Query tra cứu, kiểm tra danh sách tin bán lẻ & gói Combo kèm trạng thái kiểm soát trần giá
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-336 (BE-11.1.3)
/// </summary>
public class GetAdminListingsQueryHandler : IRequestHandler<GetAdminListingsQuery, ApiResponse<AdminListingsResponseDto>>
{
    private readonly ITicketShieldDbContext _dbContext;

    public GetAdminListingsQueryHandler(ITicketShieldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<AdminListingsResponseDto>> Handle(GetAdminListingsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page > 0 ? request.Page : 1;
        var size = request.Size > 0 ? request.Size : 20;

        // 1. Thống kê tổng hợp (Summary Metrics) toàn hệ thống cho Admin
        var baseAllQuery = _dbContext.ResaleListings.AsNoTracking();

        var totalListings = await baseAllQuery.CountAsync(cancellationToken);
        var totalSingleTickets = await baseAllQuery.CountAsync(l => l.BundleId == null, cancellationToken);
        var totalBundles = await baseAllQuery.CountAsync(l => l.BundleId != null, cancellationToken);
        var totalActiveListings = await baseAllQuery.CountAsync(l => l.ListingStatus == ListingStatus.Verified, cancellationToken);
        var totalSoldListings = await baseAllQuery.CountAsync(l => l.ListingStatus == ListingStatus.Sold, cancellationToken);

        var totalAtCeilingListings = await baseAllQuery
            .CountAsync(l => l.ResalePrice >= Math.Truncate(l.OriginalPrice * (1m + l.AppliedMarkupPercentage / 100m)), cancellationToken);

        // 2. Xây dựng truy vấn lọc danh sách tin đăng
        var query = _dbContext.ResaleListings
            .AsNoTracking()
            .Include(l => l.Event)
            .Include(l => l.Tier)
            .Include(l => l.Seller)
            .AsQueryable();

        // Lọc trạng thái tin bán
        if (request.Status.HasValue)
        {
            query = query.Where(l => l.ListingStatus == request.Status.Value);
        }

        // Lọc trạng thái xác thực
        if (request.VerificationStatus.HasValue)
        {
            query = query.Where(l => l.VerificationStatus == request.VerificationStatus.Value);
        }

        // Lọc gói combo hoặc tin lẻ
        if (request.IsBundleOnly.HasValue && request.IsBundleOnly.Value)
        {
            query = query.Where(l => l.BundleId != null);
        }
        else if (request.IsSingleOnly.HasValue && request.IsSingleOnly.Value)
        {
            query = query.Where(l => l.BundleId == null);
        }

        // Lọc theo sự kiện
        if (request.EventId.HasValue && request.EventId.Value != Guid.Empty)
        {
            query = query.Where(l => l.EventId == request.EventId.Value);
        }

        // Lọc theo người bán
        if (request.SellerId.HasValue && request.SellerId.Value != Guid.Empty)
        {
            query = query.Where(l => l.SellerId == request.SellerId.Value);
        }

        // Lọc theo chạm trần giá
        if (request.IsAtPriceCeilingOnly.HasValue && request.IsAtPriceCeilingOnly.Value)
        {
            query = query.Where(l => l.ResalePrice >= Math.Truncate(l.OriginalPrice * (1m + l.AppliedMarkupPercentage / 100m)));
        }

        // Lọc theo ngày tạo
        if (request.FromDate.HasValue)
        {
            query = query.Where(l => l.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(l => l.CreatedAt <= request.ToDate.Value);
        }

        // Tìm kiếm từ khóa
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            query = query.Where(l =>
                l.OriginalTicketCode.ToLower().Contains(kw) ||
                (l.Event != null && l.Event.Name.ToLower().Contains(kw)) ||
                (l.Seller != null && (l.Seller.FullName.ToLower().Contains(kw) || l.Seller.Email.ToLower().Contains(kw))));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // 3. Phân trang và sắp xếp mới nhất lên đầu
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        // 4. Map DTO và tính toán kiểm soát trần giá in-memory
        var dtos = items.Select(l =>
        {
            var maxAllowedPrice = ResaleListing.ComputePriceCeiling(l.OriginalPrice, l.AppliedMarkupPercentage);
            var isAtCeiling = l.ResalePrice >= maxAllowedPrice;
            var isExceeded = l.ResalePrice > maxAllowedPrice;
            var actualMarkup = l.OriginalPrice > 0
                ? Math.Round(((l.ResalePrice - l.OriginalPrice) / l.OriginalPrice) * 100m, 2)
                : 0m;

            return new AdminListingDto
            {
                ListingId = l.Id,
                BundleId = l.BundleId,
                IsBundleAllOrNothing = l.IsBundleAllOrNothing,
                BundleTotalTickets = l.BundleTotalTickets,
                EventId = l.EventId,
                EventName = l.Event?.Name ?? string.Empty,
                TierId = l.TierId,
                TierName = l.Tier?.TierName ?? string.Empty,
                OriginalTicketCode = l.OriginalTicketCode,
                MaskedTicketCode = l.MaskedTicketCode,
                SeatZone = l.SeatZone,
                TicketId = l.TicketId,
                SellerId = l.SellerId,
                SellerName = l.Seller?.FullName ?? string.Empty,
                SellerEmail = l.Seller?.Email ?? string.Empty,
                SellerPhone = l.Seller?.PhoneNumber,
                OriginalPrice = l.OriginalPrice,
                ResalePrice = l.ResalePrice,
                AppliedMarkupPercentage = l.AppliedMarkupPercentage,
                EventMaxMarkupPercentage = l.Event?.MaxResaleMarkupPercentage ?? 0m,
                MaxAllowedPrice = maxAllowedPrice,
                ActualMarkupPercentage = actualMarkup,
                IsAtPriceCeiling = isAtCeiling,
                IsPriceCeilingExceeded = isExceeded,
                DiscountAmount = l.DiscountAmount,
                DiscountPercentage = l.DiscountPercentage,
                ListingStatus = l.ListingStatus,
                VerificationStatus = l.VerificationStatus,
                IsPrivate = l.IsPrivate,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt
            };
        }).ToList();

        var paginatedList = new PaginatedList<AdminListingDto>(dtos, totalCount, page, size);

        var result = new AdminListingsResponseDto
        {
            Listings = paginatedList,
            Summary = new AdminListingsSummaryDto
            {
                TotalListings = totalListings,
                TotalSingleTickets = totalSingleTickets,
                TotalBundles = totalBundles,
                TotalActiveListings = totalActiveListings,
                TotalSoldListings = totalSoldListings,
                TotalAtCeilingListings = totalAtCeilingListings
            }
        };

        return ApiResponse<AdminListingsResponseDto>.SuccessResponse(result, "Lấy danh sách tin bán lẻ và gói combo thành công.");
    }
}

