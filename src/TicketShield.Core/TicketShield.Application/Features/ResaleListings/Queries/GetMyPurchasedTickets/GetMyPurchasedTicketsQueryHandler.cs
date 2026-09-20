using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
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
            e.Status == EscrowStatus.Disputed ||
            (e.Status == EscrowStatus.Released &&
             (!string.IsNullOrEmpty(e.BankTransactionReference) ||
              !string.IsNullOrEmpty(e.NewTicketCode))));

        var purchases = await query
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = purchases.Select(e =>
        {
            var eventName = e.Listing?.Event?.Name ?? "Concert Pass";
            var eventVenue = e.Listing?.Event?.Venue ?? "Sân Vận Động";
            var eventStart = e.Listing?.Event?.EventStartAt ?? e.CreatedAt.AddDays(30);
            var tierName = e.Listing?.Tier?.TierName ?? "Standard Pass";
            // Only BTC-issued codes after payment. Never fall back to the seller's original ticket.
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
                EscrowStatus.Released => "VALID",
                EscrowStatus.Disputed => "DISPUTED",
                EscrowStatus.Refunded => "REFUNDED",
                _ => e.Status.ToString()
            };

            return new PurchasedTicketDto
            {
                EscrowId = e.Id,
                ListingId = e.ListingId,
                EventId = e.Listing?.EventId ?? Guid.Empty,
                EventName = eventName,
                EventVenue = eventVenue,
                EventStartAt = eventStart,
                TierName = tierName,
                SeatZone = $"{tierName} • Chính chủ",
                TicketPassCode = passCode,
                TotalAmountPaid = e.TotalBuyerPaid,
                Status = status,
                PaymentReference = e.PaymentReference,
                HoldExpiresAt = null,
                RecipientName = e.RecipientName ?? e.Buyer?.FullName ?? "Buyer",
                RecipientEmail = e.RecipientEmail ?? e.Buyer?.Email ?? "",
                QrCodeData = qrPayload,
                QrCodeImageUrl = qrUrl,
                PurchasedAt = e.CreatedAt
            };
        }).ToList();

        return ApiResponse<List<PurchasedTicketDto>>.SuccessResponse(dtos, "Lấy danh sách vé đã mua thành công.");
    }
}
