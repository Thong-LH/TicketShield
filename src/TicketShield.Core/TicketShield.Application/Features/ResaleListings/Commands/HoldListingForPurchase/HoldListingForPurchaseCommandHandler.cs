using System.Security.Cryptography;
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

    public HoldListingForPurchaseCommandHandler(
        ITicketShieldDbContext dbContext,
        IResaleFeeCalculator feeCalculator,
        ICurrentUserService? currentUserService = null,
        IVietQrService? vietQrService = null)
    {
        _dbContext = dbContext;
        _feeCalculator = feeCalculator;
        _currentUserService = currentUserService;
        _vietQrService = vietQrService;
    }

    public async Task<ApiResponse<HoldListingForPurchaseResponse>> Handle(HoldListingForPurchaseCommand request, CancellationToken cancellationToken)
    {
        // 1. Authenticate Buyer
        var buyerId = _currentUserService?.UserId ?? Guid.Empty;
        if (buyerId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để giữ chỗ mua vé.");
        }

        // 2. Fetch Resale Listing
        var listing = await _dbContext.ResaleListings
            .Include(l => l.EscrowTransaction)
            .FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken);

        if (listing == null)
        {
            throw new NotFoundException("Tin đăng bán vé", request.ListingId);
        }

        // 3. Business Rule Validation: Buyer cannot be Seller
        if (listing.SellerId == buyerId)
        {
            throw new BusinessRuleViolationException("Bạn không thể giữ chỗ hoặc mua vé của chính mình.");
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
        if (listing.ListingStatus == ListingStatus.Sold || listing.ListingStatus == ListingStatus.Cancelled)
        {
            throw new BusinessRuleViolationException($"Vé này hiện ở trạng thái '{listing.ListingStatus}' và không thể đặt mua.");
        }

        if (listing.ListingStatus == ListingStatus.Transacting)
        {
            if (listing.EscrowTransaction != null &&
                listing.EscrowTransaction.UnlockAt.HasValue &&
                listing.EscrowTransaction.UnlockAt.Value > now &&
                listing.EscrowTransaction.BuyerId != buyerId)
            {
                throw new BusinessRuleViolationException("Vé này đang được giữ chỗ bởi người mua khác. Vui lòng thử lại sau.");
            }
        }

        // 6. Calculate Fees via Dynamic DynamicResaleFeeCalculator
        var feeResult = await _feeCalculator.CalculateFeeAsync(listing.ResalePrice, listing.IsPrivate, cancellationToken);

        // 7. Generate Unique Payment Reference (transfer_content for VietQR / NAPAS 247)
        var paymentReference = await GenerateUniquePaymentReferenceAsync(cancellationToken);

        // 8. Generate VietQR QuickLink (BE-CORE-3.1.2)
        var vietQrResult = _vietQrService?.GenerateSystemQuickLink(feeResult.TotalBuyerPaid, paymentReference);

        // 9. Update Listing Status & Create / Update EscrowTransaction
        var unlockAt = now.AddMinutes(10);
        listing.ListingStatus = ListingStatus.Transacting;

        EscrowTransaction escrow;
        if (listing.EscrowTransaction == null)
        {
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
                RecipientName = request.RecipientName,
                RecipientEmail = request.RecipientEmail,
                RecipientIdCard = request.RecipientIdCard
            };
            _dbContext.EscrowTransactions.Add(escrow);
            listing.EscrowTransaction = escrow;
        }
        else
        {
            escrow = listing.EscrowTransaction;
            escrow.BuyerId = buyerId;
            escrow.SellerId = listing.SellerId;
            escrow.OriginalTicketPrice = listing.ResalePrice;
            escrow.BuyerFee = feeResult.BuyerFee;
            escrow.SellerFee = feeResult.SellerFee;
            escrow.TotalBuyerPaid = feeResult.TotalBuyerPaid;
            escrow.NetSellerPayout = feeResult.NetSellerPayout;
            escrow.PaymentReference = paymentReference;
            escrow.Status = EscrowStatus.Pending;
            escrow.UnlockAt = unlockAt;
            escrow.RecipientName = request.RecipientName ?? escrow.RecipientName;
            escrow.RecipientEmail = request.RecipientEmail ?? escrow.RecipientEmail;
            escrow.RecipientIdCard = request.RecipientIdCard ?? escrow.RecipientIdCard;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

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
