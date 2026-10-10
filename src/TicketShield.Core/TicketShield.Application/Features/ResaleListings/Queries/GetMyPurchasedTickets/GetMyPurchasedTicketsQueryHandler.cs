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
                EscrowStatus.Releasing => "RELEASING",
                EscrowStatus.Released => "VALID",
                EscrowStatus.Disputed => "DISPUTED",
                EscrowStatus.Refunded => "REFUNDED",
                _ => e.Status.ToString()
            };

            var bundleId = e.BundleId ?? e.Listing?.BundleId;

            // Chỉ dựng combo từ listing THẬT trong DB. Không bao giờ suy diễn mã vé
            // bằng cách nối hậu tố (ví dụ "-T1") khi thiếu vé thật.
            List<ResaleListing> bundleListings = new();
            if (bundleId != null)
            {
                bundleListings = await _dbContext.ResaleListings
                    .AsNoTracking()
                    .Include(l => l.Tier)
                    .Where(l => l.BundleId == bundleId)
                    .OrderBy(l => l.CreatedAt)
                    .ThenBy(l => l.Id)
                    .ToListAsync(cancellationToken);
            }

            // Mã vé được cấp theo đúng thứ tự listing đã sang tên ở SePay webhook.
            var issuedCodes = SplitIssuedCodes(e.NewTicketCode);
            var issuedQrCodes = SplitIssuedCodes(e.QrCodeData);

            var effectiveBundleTotal = bundleListings.Count >= 2 ? bundleListings.Count : 1;
            var bundleItems = new List<PurchasedTicketItemDto>();

            for (var index = 0; index < bundleListings.Count; index++)
            {
                var bl = bundleListings[index];
                var itemTier = bl.Tier?.TierName ?? tierName;
                var itemSeat = !string.IsNullOrWhiteSpace(bl.SeatZone) ? bl.SeatZone : itemTier;
                var itemCode = index < issuedCodes.Count ? issuedCodes[index] : string.Empty;
                var itemQr = index < issuedQrCodes.Count ? issuedQrCodes[index] : itemCode;

                bundleItems.Add(new PurchasedTicketItemDto
                {
                    ListingId = bl.Id,
                    TicketCode = itemCode,
                    SeatZone = itemSeat,
                    QrCodeData = itemQr,
                    QrCodeImageUrl = BuildQrImageUrl(itemQr)
                });
            }

            // Vé đầu tiên của gói làm vé chính để các màn hình đơn lẻ vẫn hiển thị đúng.
            var primaryPassCode = bundleItems.Count > 0 && bundleItems[0].TicketCode.Length > 0
                ? bundleItems[0].TicketCode
                : passCode;
            var primaryQrPayload = bundleItems.Count > 0 && bundleItems[0].QrCodeData.Length > 0
                ? bundleItems[0].QrCodeData
                : qrPayload;

            dtos.Add(new PurchasedTicketDto
            {
                EscrowId = e.Id,
                ListingId = e.ListingId,
                EventId = e.Listing?.EventId ?? Guid.Empty,
                EventName = eventName,
                EventVenue = eventVenue,
                EventStartAt = eventStart,
                TierName = tierName,
                SeatZone = bundleItems.Count > 0 ? bundleItems[0].SeatZone : rawSeatZone,
                TicketPassCode = primaryPassCode,
                TotalAmountPaid = e.TotalBuyerPaid,
                Status = status,
                PaymentReference = e.PaymentReference,
                HoldExpiresAt = null,
                RecipientName = e.RecipientName ?? e.Buyer?.FullName ?? "Buyer",
                RecipientEmail = e.RecipientEmail ?? e.Buyer?.Email ?? "",
                QrCodeData = primaryQrPayload,
                QrCodeImageUrl = BuildQrImageUrl(primaryQrPayload),
                PurchasedAt = e.CreatedAt,
                BundleId = bundleId,
                BundleTotalTickets = effectiveBundleTotal >= 2 ? effectiveBundleTotal : null,
                BundleItems = bundleItems
            });
        }

        return ApiResponse<List<PurchasedTicketDto>>.SuccessResponse(dtos, "Lấy danh sách vé đã mua thành công.");
    }

    private static List<string> SplitIssuedCodes(string? rawCodes)
    {
        if (string.IsNullOrWhiteSpace(rawCodes))
        {
            return new List<string>();
        }

        return rawCodes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string BuildQrImageUrl(string payload)
    {
        return string.IsNullOrWhiteSpace(payload)
            ? string.Empty
            : $"https://api.qrserver.com/v1/create-qr-code/?size=300x300&data={Uri.EscapeDataString(payload)}";
    }
}
