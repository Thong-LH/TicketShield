using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetMyPurchasedTickets;

public class GetMyPurchasedTicketsQueryHandler : IRequestHandler<GetMyPurchasedTicketsQuery, ApiResponse<List<PurchasedTicketDto>>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ICurrentUserService? _currentUserService;

    public GetMyPurchasedTicketsQueryHandler(ITicketShieldDbContext dbContext, ICurrentUserService? currentUserService = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<List<PurchasedTicketDto>>> Handle(GetMyPurchasedTicketsQuery request, CancellationToken cancellationToken)
    {
        var buyerId = _currentUserService?.UserId ?? Guid.Empty;
        var buyerEmail = _currentUserService?.Email?.Trim().ToLowerInvariant();

        if (buyerId == Guid.Empty && string.IsNullOrWhiteSpace(buyerEmail))
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để xem danh sách vé đã mua.");
        }

        // Query successful purchases for this buyer (match by buyerId or recipientEmail or Buyer.Email)
        var query = _dbContext.EscrowTransactions
            .AsNoTracking()
            .Include(e => e.Listing)
                .ThenInclude(l => l.Event)
            .Include(e => e.Listing)
                .ThenInclude(l => l.Tier)
            .Include(e => e.Buyer)
            .AsQueryable();

        if (buyerId != Guid.Empty && !string.IsNullOrWhiteSpace(buyerEmail))
        {
            query = query.Where(e => e.BuyerId == buyerId || 
                                     (e.RecipientEmail != null && e.RecipientEmail.ToLower() == buyerEmail) || 
                                     (e.Buyer != null && e.Buyer.Email.ToLower() == buyerEmail));
        }
        else if (buyerId != Guid.Empty)
        {
            query = query.Where(e => e.BuyerId == buyerId);
        }
        else
        {
            query = query.Where(e => (e.RecipientEmail != null && e.RecipientEmail.ToLower() == buyerEmail) || 
                                     (e.Buyer != null && e.Buyer.Email.ToLower() == buyerEmail));
        }

        query = query.Where(e =>
            e.Status == EscrowStatus.Locked ||
            e.Status == EscrowStatus.Releasing ||
            e.Status == EscrowStatus.Disputed ||
            (e.Status == EscrowStatus.Released &&
             (!string.IsNullOrEmpty(e.BankTransactionReference) ||
              !string.IsNullOrEmpty(e.NewTicketCode))));

        var purchases = await query
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = new List<PurchasedTicketDto>();

        foreach (var e in purchases)
        {
            var eventName = e.Listing?.Event?.Name ?? "Concert Pass";
            var eventVenue = e.Listing?.Event?.Venue ?? "Sân Vận Động";
            var eventStart = e.Listing?.Event?.EventStartAt ?? e.CreatedAt.AddDays(30);
            var tierName = e.Listing?.Tier?.TierName ?? "Standard Pass";
            var rawSeatZone = !string.IsNullOrWhiteSpace(e.Listing?.SeatZone) ? e.Listing.SeatZone : tierName;
            var passCode = e.NewTicketCode?.Trim() ?? string.Empty;
            var qrPayload = !string.IsNullOrWhiteSpace(e.QrCodeData)
                ? e.QrCodeData.Trim()
                : passCode;

            var qrUrl = string.IsNullOrWhiteSpace(qrPayload)
                ? string.Empty
                : $"https://api.qrserver.com/v1/create-qr-code/?size=300x300&data={Uri.EscapeDataString(qrPayload)}";

            var status = e.Status switch
            {
                EscrowStatus.Pending => "PENDING_PAYMENT",
                EscrowStatus.Locked => "IN_ESCROW",
                EscrowStatus.Releasing => "IN_ESCROW",
                EscrowStatus.Released => "VALID",
                EscrowStatus.Disputed => "DISPUTED",
                EscrowStatus.Refunded => "REFUNDED",
                _ => e.Status.ToString()
            };

            var bundleId = e.BundleId ?? e.Listing?.BundleId;
            var bundleTotal = e.Listing?.BundleTotalTickets ?? 1;

            List<ResaleListing> bundleListings = new();
            if (bundleId != null)
            {
                bundleListings = await _dbContext.ResaleListings
                    .AsNoTracking()
                    .Where(l => l.BundleId == bundleId)
                    .ToListAsync(cancellationToken);
            }

            var effectiveBundleTotal = Math.Max(bundleTotal, bundleListings.Count);
            var bundleItems = new List<PurchasedTicketItemDto>();

            if (bundleListings.Count >= 2)
            {
                foreach (var bl in bundleListings)
                {
                    var itemSeat = !string.IsNullOrWhiteSpace(bl.SeatZone) ? bl.SeatZone : tierName;
                    bundleItems.Add(new PurchasedTicketItemDto
                    {
                        ListingId = bl.Id,
                        TicketCode = passCode,
                        SeatZone = itemSeat,
                        QrCodeData = qrPayload,
                        QrCodeImageUrl = qrUrl
                    });
                }
            }
            else if (effectiveBundleTotal >= 2)
            {
                var seats = rawSeatZone.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (int i = 0; i < effectiveBundleTotal; i++)
                {
                    var seatText = seats.Length > i ? seats[i] : (seats.Length > 0 ? $"{seats[0]} (#{i + 1})" : $"{tierName} (Seat {i + 1})");
                    bundleItems.Add(new PurchasedTicketItemDto
                    {
                        ListingId = e.ListingId,
                        TicketCode = !string.IsNullOrEmpty(passCode) ? $"{passCode}-T{i + 1}" : string.Empty,
                        SeatZone = seatText,
                        QrCodeData = qrPayload,
                        QrCodeImageUrl = qrUrl
                    });
                }
            }

            dtos.Add(new PurchasedTicketDto
            {
                EscrowId = e.Id,
                ListingId = e.ListingId,
                EventId = e.Listing?.EventId ?? Guid.Empty,
                EventName = eventName,
                EventVenue = eventVenue,
                EventStartAt = eventStart,
                TierName = tierName,
                SeatZone = rawSeatZone,
                TicketPassCode = passCode,
                TotalAmountPaid = e.TotalBuyerPaid,
                Status = status,
                PaymentReference = e.PaymentReference,
                HoldExpiresAt = null,
                RecipientName = e.RecipientName ?? e.Buyer?.FullName ?? "Buyer",
                RecipientEmail = e.RecipientEmail ?? e.Buyer?.Email ?? "",
                QrCodeData = qrPayload,
                QrCodeImageUrl = qrUrl,
                PurchasedAt = e.CreatedAt,
                BundleId = bundleId,
                BundleTotalTickets = effectiveBundleTotal >= 2 ? effectiveBundleTotal : null,
                BundleItems = bundleItems
            });
        }

        return ApiResponse<List<PurchasedTicketDto>>.SuccessResponse(dtos, "Lấy danh sách vé đã mua thành công.");
    }
}
