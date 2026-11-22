using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetSellerListings;

public class GetSellerListingsQueryHandler : IRequestHandler<GetSellerListingsQuery, ApiResponse<List<SellerListingDto>>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ICurrentUserService? _currentUserService;

    public GetSellerListingsQueryHandler(ITicketShieldDbContext dbContext, ICurrentUserService? currentUserService = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<List<SellerListingDto>>> Handle(GetSellerListingsQuery request, CancellationToken cancellationToken)
    {
        // 1. Resolve SellerId from current authenticated user — no anonymous fallback
        var sellerId = _currentUserService?.UserId ?? Guid.Empty;
        if (sellerId == Guid.Empty)
        {
            throw new UnauthorizedException("You must be signed in to view your ticket listings.");
        }

        // 2. Query seller's resale listings with Event and Tier navigation
        var query = _dbContext.ResaleListings
            .AsNoTracking()
            .Include(l => l.Event)
            .Include(l => l.Tier)
            .Include(l => l.Seller)
            .Include(l => l.EscrowTransactions)
                .ThenInclude(e => e.PayoutTransaction)
            .Include(l => l.EscrowTransactions)
                .ThenInclude(e => e.Dispute)
            .Include(l => l.EscrowListings)
                .ThenInclude(el => el.Escrow)
                    .ThenInclude(e => e.PayoutTransaction)
            .Include(l => l.EscrowListings)
                .ThenInclude(el => el.Escrow)
                    .ThenInclude(e => e.Dispute)
            .Where(l => l.SellerId == sellerId);

        if (request.Status.HasValue)
        {
            query = query.Where(l => l.ListingStatus == request.Status.Value);
        }

        var listings = await query
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

        // Đảm bảo lấy được Escrow của tất cả các vé trong combo/bundle dù qua EscrowTransactionListings hay BundleId
        var listingIds = listings.Select(l => l.Id).ToList();
        var extraEscrowListings = await _dbContext.EscrowTransactionListings
            .AsNoTracking()
            .Include(etl => etl.Escrow)
                .ThenInclude(e => e.PayoutTransaction)
            .Include(etl => etl.Escrow)
                .ThenInclude(e => e.Dispute)
            .Where(etl => listingIds.Contains(etl.ListingId))
            .ToListAsync(cancellationToken);

        var escrowByListingId = extraEscrowListings
            .GroupBy(etl => etl.ListingId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(etl => etl.Escrow).Where(e => e != null).OrderByDescending(e => e.CreatedAt).FirstOrDefault()
            );

        var bundleIds = listings.Where(l => l.BundleId.HasValue).Select(l => l.BundleId!.Value).Distinct().ToList();
        var escrowByBundleId = new Dictionary<Guid, EscrowTransaction>();
        if (bundleIds.Count > 0)
        {
            var bundleEscrows = await _dbContext.EscrowTransactions
                .AsNoTracking()
                .Include(e => e.PayoutTransaction)
                .Include(e => e.Dispute)
                .Where(e => e.BundleId.HasValue && bundleIds.Contains(e.BundleId.Value))
                .ToListAsync(cancellationToken);

            escrowByBundleId = bundleEscrows
                .Where(e => e.BundleId.HasValue)
                .GroupBy(e => e.BundleId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(e => e.CreatedAt).First()
                );
        }

        // 3. Map entities to DTOs
        var dtos = listings.Select(l =>
        {
            var directEscrow = l.EscrowTransactions
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefault();

            escrowByListingId.TryGetValue(l.Id, out var mappedEscrow);
            EscrowTransaction? bundleEscrow = null;
            if (l.BundleId.HasValue)
            {
                escrowByBundleId.TryGetValue(l.BundleId.Value, out bundleEscrow);
            }

            var latestEscrow = directEscrow ?? mappedEscrow ?? bundleEscrow;

            // Xử lý chia tỷ lệ tài chính cho từng vé trong trường hợp mua gói Combo nhiều vé
            decimal? netSellerPayout = null;
            decimal? sellerFee = null;
            decimal? buyerFee = null;
            decimal? totalBuyerPaid = null;

            if (latestEscrow != null)
            {
                if (latestEscrow.BundleId.HasValue && latestEscrow.OriginalTicketPrice > 0)
                {
                    var ratio = l.ResalePrice / latestEscrow.OriginalTicketPrice;
                    netSellerPayout = Math.Round(latestEscrow.NetSellerPayout * ratio, MidpointRounding.AwayFromZero);
                    sellerFee = Math.Round(latestEscrow.SellerFee * ratio, MidpointRounding.AwayFromZero);
                    buyerFee = Math.Round(latestEscrow.BuyerFee * ratio, MidpointRounding.AwayFromZero);
                    totalBuyerPaid = Math.Round(latestEscrow.TotalBuyerPaid * ratio, MidpointRounding.AwayFromZero);
                }
                else
                {
                    netSellerPayout = latestEscrow.NetSellerPayout;
                    sellerFee = latestEscrow.SellerFee;
                    buyerFee = latestEscrow.BuyerFee;
                    totalBuyerPaid = latestEscrow.TotalBuyerPaid;
                }
            }

            return new SellerListingDto
            {
                ListingId = l.Id,
                EventId = l.EventId,
                EventName = l.Event?.Name ?? string.Empty,
                EventVenue = l.Event?.Venue ?? string.Empty,
                EventStartAt = l.Event?.EventStartAt ?? default,
                TierId = l.TierId,
                TierName = l.Tier?.TierName ?? string.Empty,
                SeatZone = l.SeatZone,
                OriginalTicketCode = l.OriginalTicketCode,
                OriginalPrice = l.OriginalPrice,
                ResalePrice = l.ResalePrice,
                DiscountAmount = l.DiscountAmount,
                DiscountPercentage = l.DiscountPercentage,
                IsPrivate = l.IsPrivate,
                PrivateAccessToken = l.PrivateAccessToken,
                ShareUrl = l.IsPrivate && !string.IsNullOrEmpty(l.PrivateAccessToken)
                    ? $"/p/{l.PrivateAccessToken}"
                    : null,
                VerificationStatus = l.VerificationStatus.ToString(),
                ListingStatus = l.ListingStatus.ToString(),
                EscrowStatus = latestEscrow?.Status.ToString(),
                NetSellerPayout = netSellerPayout,
                SellerFee = sellerFee,
                BuyerFee = buyerFee,
                TotalBuyerPaid = totalBuyerPaid,
                UnlockAt = latestEscrow?.UnlockAt,
                InSettlementBuffer = latestEscrow != null && latestEscrow.Status == Domain.Enums.EscrowStatus.Locked,
                PayoutStatus = latestEscrow?.PayoutTransaction?.Status.ToString(),
                PayoutProcessedAt = latestEscrow?.PayoutTransaction?.ProcessedAt,
                PayoutCode = latestEscrow?.PayoutTransaction?.PayoutCode,
                PayoutAccountName = latestEscrow?.PayoutTransaction?.RecipientAccountName ?? l.Seller?.PayoutAccountName,
                PayoutBankCode = latestEscrow?.PayoutTransaction?.RecipientBankCode ?? l.Seller?.PayoutBankCode,
                PayoutAccountNumber = latestEscrow?.PayoutTransaction?.RecipientAccountNumber ?? l.Seller?.PayoutAccountNumber,
                BankTransactionReference = latestEscrow?.BankTransactionReference,
                DisputeId = latestEscrow?.Dispute?.Id,
                PayoutBankInfo = latestEscrow?.PayoutTransaction != null
                    ? $"{latestEscrow.PayoutTransaction.RecipientBankCode} - {latestEscrow.PayoutTransaction.RecipientAccountNumber}"
                    : (!string.IsNullOrEmpty(l.Seller?.PayoutBankCode) && !string.IsNullOrEmpty(l.Seller?.PayoutAccountNumber)
                        ? $"{l.Seller.PayoutBankCode} - {l.Seller.PayoutAccountNumber}"
                        : null),
                CreatedAt = l.CreatedAt,
                BundleId = l.BundleId,
                IsBundleAllOrNothing = l.IsBundleAllOrNothing,
                BundleTotalTickets = l.BundleTotalTickets >= 2 ? l.BundleTotalTickets : null
            };
        }).ToList();

        return ApiResponse<List<SellerListingDto>>.SuccessResponse(dtos, "Seller ticket listings retrieved successfully.");
    }
}
