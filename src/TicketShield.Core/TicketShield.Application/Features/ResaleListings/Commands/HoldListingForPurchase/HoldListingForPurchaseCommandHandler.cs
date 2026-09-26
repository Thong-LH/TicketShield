using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;

public class HoldListingForPurchaseCommandHandler : IRequestHandler<HoldListingForPurchaseCommand, ApiResponse<HoldListingForPurchaseResponse>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ICurrentUserService? _currentUserService;
    private readonly IResaleFeeCalculator _feeCalculator;
    private readonly IVietQrService? _vietQrService;
    private readonly IMessageSchedulerService? _messageSchedulerService;

    public HoldListingForPurchaseCommandHandler(
        ITicketShieldDbContext dbContext,
        IResaleFeeCalculator feeCalculator,
        ICurrentUserService? currentUserService = null,
        IVietQrService? vietQrService = null,
        IMessageSchedulerService? messageSchedulerService = null)
    {
        _dbContext = dbContext;
        _feeCalculator = feeCalculator;
        _currentUserService = currentUserService;
        _vietQrService = vietQrService;
        _messageSchedulerService = messageSchedulerService;
    }

    public async Task<ApiResponse<HoldListingForPurchaseResponse>> Handle(HoldListingForPurchaseCommand request, CancellationToken cancellationToken)
    {
        // 1. Authenticate Buyer from JWT
        var buyerId = _currentUserService?.UserId ?? Guid.Empty;
        if (buyerId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để giữ chỗ mua vé.");
        }

        var buyer = await _dbContext.ShadowUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == buyerId, cancellationToken);

        if (buyer == null)
        {
            var email = _currentUserService?.Email ?? request.RecipientEmail ?? $"{buyerId}@ticketshield.vn";
            var fullName = !string.IsNullOrWhiteSpace(request.RecipientName) ? request.RecipientName : "TicketShield Buyer";
            await _dbContext.EnsureShadowUserExistsAsync(buyerId, email, fullName, cancellationToken);

            buyer = await _dbContext.ShadowUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == buyerId, cancellationToken);
        }

        if (buyer != null && !buyer.IsActive)
        {
            throw new UnauthorizedException("Tài khoản của bạn đã bị vô hiệu hóa.");
        }

        // Concurrency Control: Acquire PostgreSQL transaction advisory lock hashed by ListingId
        await using var tx = await _dbContext.BeginAdvisoryLockTransactionAsync(ComputeLockKey(request.ListingId), cancellationToken);

        try
        {
            // 2. Fetch Resale Listing
            var listing = await _dbContext.ResaleListings
                .Include(l => l.EscrowTransactions)
                .Include(l => l.Event)
                .FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken);

            if (listing == null)
            {
                throw new NotFoundException("Tin đăng bán vé", request.ListingId);
            }

            // 3. Business Rule Validation: Buyer cannot be Seller (BE-CORE-3.1.6)
            if (listing.SellerId == buyerId)
            {
                throw new BadRequestException("Bạn không thể tự mua vé của chính mình.");
            }

            // 4. Validate Private Access Token if listing is private
            if (listing.IsPrivate)
            {
                if (string.IsNullOrWhiteSpace(request.PrivateAccessToken) || listing.PrivateAccessToken != request.PrivateAccessToken)
                {
                    throw new ForbiddenAccessException("Mã truy cập vé riêng tư không hợp lệ hoặc bị thiếu.");
                }
            }

            // 5. Validate Listing Status & Active 10-Minute Lock
            var now = DateTimeOffset.UtcNow;
            if (listing.ListingStatus == ListingStatus.Sold ||
                listing.ListingStatus == ListingStatus.Cancelled ||
                listing.ListingStatus == ListingStatus.Expired)
            {
                throw new BusinessRuleViolationException($"Vé này hiện ở trạng thái '{listing.ListingStatus}' và không thể đặt mua.");
            }

            // BR-L04: Enforce Event Resale Deadline — chặn nếu sự kiện cận giờ (< 2h) hoặc đã qua
            var eventStartAt = listing.Event?.EventStartAt;
            if (eventStartAt.HasValue && eventStartAt.Value.AddHours(-2) <= now)
            {
                throw new BusinessRuleViolationException(
                    $"Không thể đặt mua vé. Sự kiện sẽ bắt đầu lúc {eventStartAt.Value:dd/MM/yyyy HH:mm} UTC và đã qua thời hạn mua vé (trước 2 giờ khai mạc).");
            }

            var activePendingEscrow = listing.EscrowTransactions
                .Where(e => e.Status == EscrowStatus.Pending && e.UnlockAt.HasValue && e.UnlockAt.Value > now)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefault();

            if (listing.ListingStatus == ListingStatus.Transacting)
            {
                if (activePendingEscrow != null && activePendingEscrow.BuyerId != buyerId)
                {
                    throw new BusinessRuleViolationException("Vé này đang được giữ chỗ bởi người mua khác. Vui lòng thử lại sau.");
                }
            }

            // 6. Calculate Fees via Dynamic DynamicResaleFeeCalculator
            var feeResult = await _feeCalculator.CalculateFeeAsync(listing.ResalePrice, listing.IsPrivate, cancellationToken);

            // 7. Determine Payment Reference (transfer_content for VietQR / NAPAS 247)
            string paymentReference;
            if (activePendingEscrow != null && activePendingEscrow.BuyerId == buyerId && !string.IsNullOrWhiteSpace(activePendingEscrow.PaymentReference))
            {
                paymentReference = activePendingEscrow.PaymentReference;
            }
            else
            {
                paymentReference = await GenerateUniquePaymentReferenceAsync(cancellationToken);
            }

            // 8. Generate VietQR QuickLink (BE-CORE-3.1.2)
            var vietQrResult = _vietQrService?.GenerateSystemQuickLink(feeResult.TotalBuyerPaid, paymentReference);

            // 9. Update Listing Status & Create / Renew EscrowTransaction
            var unlockAt = now.AddMinutes(10);
            listing.ListingStatus = ListingStatus.Transacting;

            EscrowTransaction escrow;
            if (activePendingEscrow != null && activePendingEscrow.BuyerId == buyerId)
            {
                // Same buyer renewing their existing active hold session
                escrow = activePendingEscrow;
                escrow.UnlockAt = unlockAt;
                if (!string.IsNullOrWhiteSpace(request.RecipientName)) escrow.RecipientName = request.RecipientName;
                if (!string.IsNullOrWhiteSpace(request.RecipientEmail)) escrow.RecipientEmail = request.RecipientEmail;
                if (!string.IsNullOrWhiteSpace(request.RecipientIdCard)) escrow.RecipientIdCard = request.RecipientIdCard;
            }
            else
            {
                // Always create a NEW independent EscrowTransaction for new hold attempts.
                // Preserves historical records (especially RefundQueued, Cancelled, Expired) intact.
                escrow = new EscrowTransaction
                {
                    Id = Guid.NewGuid(),
                    ListingId = listing.Id,
                    BuyerId = buyerId,
                    SellerId = listing.SellerId,
                    OriginalTicketPrice = listing.ResalePrice,
                    BuyerFee = feeResult.BuyerFee,
                    SellerFee = feeResult.SellerFee,
                    TotalBuyerPaid = feeResult.TotalBuyerPaid,
                    NetSellerPayout = feeResult.NetSellerPayout,
                    PaymentReference = paymentReference,
                    Status = EscrowStatus.Pending,
                    UnlockAt = unlockAt,
                    RecipientName = !string.IsNullOrWhiteSpace(request.RecipientName) ? request.RecipientName : buyer.FullName,
                    RecipientEmail = !string.IsNullOrWhiteSpace(request.RecipientEmail) ? request.RecipientEmail : buyer.Email,
                    RecipientIdCard = request.RecipientIdCard
                };
                _dbContext.EscrowTransactions.Add(escrow);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            if (_messageSchedulerService != null)
            {
                await _messageSchedulerService.ScheduleHoldExpiryAsync(escrow.Id, listing.Id, unlockAt, cancellationToken);
            }

            var response = new HoldListingForPurchaseResponse
            {
                EscrowId = escrow.Id,
                ListingId = listing.Id,
                ListingStatus = listing.ListingStatus.ToString(),
                PaymentReference = paymentReference,
                QrImageUrl = vietQrResult?.QrImageUrl ?? string.Empty,
                QuickLinkUrl = vietQrResult?.QuickLinkUrl ?? string.Empty,
                BankBin = vietQrResult?.BankBin ?? string.Empty,
                AccountNumber = vietQrResult?.AccountNumber ?? string.Empty,
                AccountName = vietQrResult?.AccountName ?? string.Empty,
                ResalePrice = listing.ResalePrice,
                BuyerFee = feeResult.BuyerFee,
                SellerFee = feeResult.SellerFee,
                TotalBuyerPaid = feeResult.TotalBuyerPaid,
                NetSellerPayout = feeResult.NetSellerPayout,
                UnlockAt = unlockAt,
                HoldDurationSeconds = (int)Math.Max(0, (unlockAt - DateTimeOffset.UtcNow).TotalSeconds)
            };

            return ApiResponse<HoldListingForPurchaseResponse>.SuccessResponse(
                response,
                "Giữ chỗ vé thành công! Vui lòng thanh toán trong vòng 10 phút.");
        }
        catch
        {
            if (tx != null)
            {
                await tx.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }


    private static long ComputeLockKey(Guid listingId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ts:hold:listing:" + listingId));
        return BitConverter.ToInt64(hash, 0);
    }

    private async Task<string> GenerateUniquePaymentReferenceAsync(CancellationToken cancellationToken)
    {
        const string chars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // Clear alphanumeric chars
        string code;
        bool exists;
        int maxAttempts = 10;
        int attempts = 0;

        do
        {
            var randomBytes = new byte[8];
            RandomNumberGenerator.Fill(randomBytes);
            var charArray = new char[8];
            for (int i = 0; i < 8; i++)
            {
                charArray[i] = chars[randomBytes[i] % chars.Length];
            }

            code = "TS" + new string(charArray);
            exists = await _dbContext.EscrowTransactions.AnyAsync(e => e.PaymentReference == code, cancellationToken);
            attempts++;
        }
        while (exists && attempts < maxAttempts);

        if (exists)
        {
            code = $"TS{DateTimeOffset.UtcNow.Ticks.ToString()[^8..]}";
        }

        return code;
    }
}

