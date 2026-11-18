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
    private readonly IPaymentRealtimeNotifier? _paymentNotifier;

    public HoldListingForPurchaseCommandHandler(
        ITicketShieldDbContext dbContext,
        IResaleFeeCalculator feeCalculator,
        ICurrentUserService? currentUserService = null,
        IVietQrService? vietQrService = null,
        IMessageSchedulerService? messageSchedulerService = null, IPaymentRealtimeNotifier? paymentNotifier = null)
    {
        _dbContext = dbContext;
        _feeCalculator = feeCalculator;
        _currentUserService = currentUserService;
        _vietQrService = vietQrService;
        _messageSchedulerService = messageSchedulerService;
        _paymentNotifier = paymentNotifier;
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
            .FirstOrDefaultAsync(u => u.Id == buyerId, cancellationToken);

        if (buyer == null)
        {
            if (_currentUserService != null && !string.IsNullOrWhiteSpace(_currentUserService.Email))
            {
                var roleParsed = Enum.TryParse<UserRole>(_currentUserService.Role, out var r) ? r : UserRole.User;
                buyer = new ShadowUser
                {
                    Id = buyerId,
                    Email = _currentUserService.Email,
                    FullName = string.IsNullOrWhiteSpace(_currentUserService.FullName) ? _currentUserService.Email : _currentUserService.FullName,
                    Role = roleParsed,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.ShadowUsers.AddAsync(buyer, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new UnauthorizedException("Tài khoản người dùng không tồn tại hoặc chưa được đồng bộ.");
            }
        }

        if (!buyer.IsActive)
        {
            throw new UnauthorizedException("Tài khoản của bạn đã bị vô hiệu hóa.");
        }

        // 2. Route bundle listings before opening the single-listing transaction.
        // Bundle hold opens its own bundle-scoped advisory lock below; nesting EF transactions
        // on PostgreSQL causes a 500 before the checkout QR can be generated.
        var routingListing = await _dbContext.ResaleListings
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken);

        if (routingListing == null)
        {
            throw new NotFoundException("Tin đăng bán vé", request.ListingId);
        }

        var isBundle = routingListing.BundleId != null || routingListing.BundleTotalTickets >= 2;
        if (isBundle)
        {
            return await HandleBundleHoldAsync(routingListing, buyer, request, cancellationToken);
        }

        // 3. Concurrency Control: Acquire PostgreSQL transaction advisory lock hashed by ListingId for single listing
        await using var tx = await _dbContext.BeginAdvisoryLockTransactionAsync(ComputeLockKey(request.ListingId), cancellationToken);

        try
        {
            // 3.1 Fetch Resale Listing FRESH inside the locked transaction to guarantee we inspect fresh state
            var listing = await _dbContext.ResaleListings
                .Include(l => l.EscrowTransactions)
                .Include(l => l.Event)
                .FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken);

            if (listing == null)
            {
                throw new NotFoundException("Tin đăng bán vé", request.ListingId);
            }

            // 4. Business Rule Validation: Buyer cannot be Seller (BE-CORE-3.1.6)
            if (listing.SellerId == buyerId)
            {
                throw new BadRequestException("Bạn không thể tự mua vé của chính mình.");
            }

            // Ensure Seller exists in ShadowUsers table to prevent FK violations
            await EnsureUserExistsInShadowUsersAsync(listing.SellerId, cancellationToken);

            // 5. Validate Private Access Token if listing is private
            if (listing.IsPrivate)
            {
                if (string.IsNullOrWhiteSpace(request.PrivateAccessToken) || listing.PrivateAccessToken != request.PrivateAccessToken)
                {
                    throw new ForbiddenAccessException("Mã truy cập vé riêng tư không hợp lệ hoặc bị thiếu.");
                }
            }

            // 6. Validate Listing Status & Active 10-Minute Lock
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

            // BE-12.1.6: Chặn đặt mua mới nếu vé có Escrow đang đối soát hoặc đã khóa thanh toán
            var hasBlockingEscrow = await _dbContext.EscrowTransactionListings
                .AnyAsync(etl => etl.ListingId == listing.Id &&
                                 (etl.Escrow.Status == EscrowStatus.TransferNeedsReview || etl.Escrow.Status == EscrowStatus.Locked), cancellationToken)
                || listing.EscrowTransactions
                    .Any(e => e.Status == EscrowStatus.TransferNeedsReview || e.Status == EscrowStatus.Locked);
            if (hasBlockingEscrow)
            {
                throw new BusinessRuleViolationException("Vé này đang trong quá trình thanh toán hoặc đối soát, không thể đặt mua.");
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

            // 7. Calculate Fees via Dynamic DynamicResaleFeeCalculator
            var feeResult = await _feeCalculator.CalculateFeeAsync(listing.ResalePrice, listing.IsPrivate, listing.EventId, cancellationToken);

            // 8. Determine Payment Reference (transfer_content for VietQR / NAPAS 247)
            string paymentReference;
            if (activePendingEscrow != null && activePendingEscrow.BuyerId == buyerId && !string.IsNullOrWhiteSpace(activePendingEscrow.PaymentReference))
            {
                paymentReference = activePendingEscrow.PaymentReference;
            }
            else
            {
                paymentReference = await GenerateUniquePaymentReferenceAsync(cancellationToken);
            }

            // 9. Generate VietQR QuickLink (BE-CORE-3.1.2)
            var vietQrResult = _vietQrService?.GenerateSystemQuickLink(feeResult.TotalBuyerPaid, paymentReference);

            // 10. Update Listing Status & Create / Renew EscrowTransaction
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
                _dbContext.EscrowTransactionListings.Add(new EscrowTransactionListing
                {
                    Id = Guid.NewGuid(),
                    EscrowId = escrow.Id,
                    ListingId = listing.Id
                });
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
            if (_paymentNotifier != null) await _paymentNotifier.NotifyMarketplaceListingChangedAsync(new { listingId = listing.Id }, cancellationToken);

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
                HoldDurationSeconds = (int)Math.Max(0, (unlockAt - DateTimeOffset.UtcNow).TotalSeconds),
                ServerNow = now
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

    /// <summary>
    /// BE-CORE-5.2.3: Atomic hold for AllOrNothing bundle.
    /// Locks all N listings in the bundle to TRANSACTING in 1 DB transaction,
    /// creates 1 aggregated EscrowTransaction with summed fees.
    /// </summary>
    private async Task<ApiResponse<HoldListingForPurchaseResponse>> HandleBundleHoldAsync(
        ResaleListing anchorListing,
        ShadowUser buyer,
        HoldListingForPurchaseCommand request,
        CancellationToken cancellationToken)
    {
        var buyerId = buyer.Id;
        var bundleId = anchorListing.BundleId ?? anchorListing.Id;

        // Ensure Seller exists in ShadowUsers table to prevent FK violations
        await EnsureUserExistsInShadowUsersAsync(anchorListing.SellerId, cancellationToken);

        // 1. Concurrency Control: Acquire advisory lock by BundleId BEFORE querying and validating
        await using var bundleTx = await _dbContext.BeginAdvisoryLockTransactionAsync(ComputeBundleLockKey(bundleId), cancellationToken);

        try
        {
            // 2. Query all listings in the bundle fresh inside the locked transaction
            var bundleListings = await _dbContext.ResaleListings
                .Include(l => l.EscrowTransactions)
                .Include(l => l.Event)
                .Where(l => (anchorListing.BundleId != null && l.BundleId == anchorListing.BundleId) || l.Id == anchorListing.Id)
                .OrderBy(l => l.CreatedAt)
                .ThenBy(l => l.Id)
                .ToListAsync(cancellationToken);

            // Chỉ tính giá và cấp vé trên danh sách listing THẬT của gói.
            // BundleTotalTickets khai trong listing không được phép vượt quá số listing thật,
            // tránh tình trạng "1 vé thật nhưng tính tiền N vé" hoặc sinh mã vé giả.
            if (bundleListings.Count < 2 || bundleListings.Count > ResaleListing.MaxBundleTickets)
            {
                throw new BusinessRuleViolationException(
                    $"Gói vé này không hợp lệ (cần từ 2 đến {ResaleListing.MaxBundleTickets} vé, thực tế tìm thấy {bundleListings.Count} vé).");
            }

            var claimedTotal = anchorListing.BundleTotalTickets;
            if (claimedTotal != bundleListings.Count)
            {
                throw new BusinessRuleViolationException(
                    $"Gói vé không hợp lệ: khai báo {claimedTotal} vé nhưng hệ thống chỉ tìm thấy {bundleListings.Count} vé thật. Vui lòng thử lại.");
            }

            var effectiveBundleTotal = bundleListings.Count;

            // 2.1 Determine selected listings to hold
            List<ResaleListing> selectedListings;

            if (request.SelectedListingIds != null && request.SelectedListingIds.Count > 0)
            {
                if (request.SelectedListingIds.Distinct().Count() != request.SelectedListingIds.Count)
                {
                    throw new BusinessRuleViolationException("Danh sách vé chọn chứa mã trùng lặp.");
                }

                var bundleMap = bundleListings.ToDictionary(l => l.Id);
                var invalidIds = request.SelectedListingIds.Where(id => !bundleMap.ContainsKey(id)).ToList();
                if (invalidIds.Count > 0)
                {
                    throw new BusinessRuleViolationException(
                        $"Danh sách vé chọn chứa vé không thuộc gói vé này (ListingId: {string.Join(", ", invalidIds)}).");
                }

                if (anchorListing.IsBundleAllOrNothing && request.SelectedListingIds.Count != bundleListings.Count)
                {
                    throw new BusinessRuleViolationException(
                        $"Gói vé này yêu cầu mua toàn bộ gói ({bundleListings.Count} vé), không được chọn mua lẻ.");
                }

                selectedListings = request.SelectedListingIds.Select(id => bundleMap[id]).ToList();
            }
            else
            {
                if (!anchorListing.IsBundleAllOrNothing && request.BuyAsBundle == false)
                {
                    selectedListings = new List<ResaleListing> { anchorListing };
                }
                else
                {
                    selectedListings = bundleListings;
                }
            }

            // BE-12.1.6: Chặn đặt mua mới nếu vé người mua đang chọn nằm trong Escrow đang đối soát hoặc đã khóa thanh toán
            var selectedListingIds = selectedListings.Select(s => s.Id).ToList();
            var hasBlockingSelectedEscrow = await _dbContext.EscrowTransactionListings
                .AnyAsync(etl => selectedListingIds.Contains(etl.ListingId) &&
                               (etl.Escrow.Status == EscrowStatus.TransferNeedsReview || etl.Escrow.Status == EscrowStatus.Locked), cancellationToken);

            if (!hasBlockingSelectedEscrow)
            {
                hasBlockingSelectedEscrow = await _dbContext.EscrowTransactions
                    .AnyAsync(e => selectedListingIds.Contains(e.ListingId) &&
                                   (e.Status == EscrowStatus.TransferNeedsReview || e.Status == EscrowStatus.Locked), cancellationToken);
            }

            if (hasBlockingSelectedEscrow)
            {
                throw new BusinessRuleViolationException("Vé này đang trong quá trình thanh toán hoặc đối soát, không thể đặt mua.");
            }

            // 3. Per-listing validation
            var now = DateTimeOffset.UtcNow;

            // Holding any subset of a flexible bundle temporarily locks the
            // complete bundle. The escrow is only linked to the tickets the
            // buyer selected, so checking a child listing alone would allow a
            // second buyer to create a competing checkout for an unselected
            // sibling while the first payment is still pending.
            var otherActiveBundleHold = await _dbContext.EscrowTransactions
                .AnyAsync(e => e.BundleId == bundleId &&
                               e.Status == EscrowStatus.Pending &&
                               e.UnlockAt.HasValue &&
                               e.UnlockAt.Value > now &&
                               e.BuyerId != buyerId,
                    cancellationToken);
            if (otherActiveBundleHold)
            {
                throw new BusinessRuleViolationException(
                    "Gói vé này đang được giữ chỗ bởi người mua khác. Vui lòng thử lại sau.");
            }

            if (bundleListings.Any(l => l.SellerId == buyerId))
            {
                throw new BadRequestException("Bạn không thể tự mua vé của chính mình.");
            }

            foreach (var listing in selectedListings)
            {
                if (listing.IsPrivate)
                {
                    var isTokenValid = !string.IsNullOrWhiteSpace(request.PrivateAccessToken) &&
                                       (string.Equals(listing.PrivateAccessToken, request.PrivateAccessToken, StringComparison.OrdinalIgnoreCase) ||
                                        bundleListings.Any(b => b.IsPrivate && string.Equals(b.PrivateAccessToken, request.PrivateAccessToken, StringComparison.OrdinalIgnoreCase)));

                    if (!isTokenValid)
                    {
                        throw new ForbiddenAccessException("Mã truy cập vé riêng tư không hợp lệ hoặc bị thiếu.");
                    }
                }

                if (listing.ListingStatus == ListingStatus.Sold ||
                    listing.ListingStatus == ListingStatus.Cancelled ||
                    listing.ListingStatus == ListingStatus.Expired)
                {
                    throw new BusinessRuleViolationException($"Vé '{listing.OriginalTicketCode}' trong gói đang ở trạng thái '{listing.ListingStatus}' và không thể đặt mua.");
                }

                var eventStartAt = listing.Event?.EventStartAt;
                if (eventStartAt.HasValue && eventStartAt.Value.AddHours(-2) <= now)
                {
                    throw new BusinessRuleViolationException(
                        $"Không thể đặt mua gói vé. Sự kiện sẽ bắt đầu lúc {eventStartAt.Value:dd/MM/yyyy HH:mm} UTC và đã qua thời hạn mua vé.");
                }

                if (listing.ListingStatus == ListingStatus.Transacting)
                {
                    var otherHold = listing.EscrowTransactions
                        .Any(e => e.Status == EscrowStatus.Pending && e.UnlockAt.HasValue && e.UnlockAt.Value > now && e.BuyerId != buyerId);
                    if (otherHold)
                    {
                        throw new BusinessRuleViolationException("Gói vé này đang được giữ chỗ bởi người mua khác. Vui lòng thử lại sau.");
                    }
                }
            }

            if (anchorListing.IsBundleAllOrNothing)
            {
                foreach (var listing in bundleListings)
                {
                    if (listing.ListingStatus == ListingStatus.Sold ||
                        listing.ListingStatus == ListingStatus.Cancelled ||
                        listing.ListingStatus == ListingStatus.Expired)
                    {
                        throw new BusinessRuleViolationException($"Vé '{listing.OriginalTicketCode}' trong gói đang ở trạng thái '{listing.ListingStatus}' và không thể đặt mua.");
                    }
                }
            }

            // 4. Check for existing bundle escrow (renew scenario)
            var existingBundleEscrow = await _dbContext.EscrowTransactions
                .Where(e => e.BundleId == bundleId && e.BuyerId == buyerId && e.Status == EscrowStatus.Pending
                            && e.UnlockAt.HasValue && e.UnlockAt.Value > now)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var isRenew = existingBundleEscrow != null;

            // 5. Calculate aggregated fees ONLY for selected listings
            var unlockAt = now.AddMinutes(10);
            decimal totalOriginalPrice = 0m;
            decimal totalBuyerFee = 0m;
            decimal totalSellerFee = 0m;
            decimal totalBuyerPaid = 0m;
            decimal totalNetSellerPayout = 0m;
            var bundleItems = new List<BundleHeldItemDto>();

            foreach (var listing in selectedListings)
            {
                var feeResult = await _feeCalculator.CalculateFeeAsync(listing.ResalePrice, listing.IsPrivate, listing.EventId, cancellationToken);
                totalOriginalPrice += listing.ResalePrice;
                totalBuyerFee += feeResult.BuyerFee;
                totalSellerFee += feeResult.SellerFee;
                totalBuyerPaid += feeResult.TotalBuyerPaid;
                totalNetSellerPayout += feeResult.NetSellerPayout;

                bundleItems.Add(new BundleHeldItemDto
                {
                    ListingId = listing.Id,
                    ResalePrice = listing.ResalePrice,
                    BuyerFee = feeResult.BuyerFee,
                    SellerFee = feeResult.SellerFee
                });

                listing.ListingStatus = ListingStatus.Transacting;
            }

            // Đồng thời tạm khóa các vé còn lại trong bundle đang Verified sang Transacting để tránh tranh chấp khi checkout
            foreach (var listing in bundleListings.Where(l => !selectedListings.Any(s => s.Id == l.Id)))
            {
                if (listing.ListingStatus == ListingStatus.Verified)
                {
                    listing.ListingStatus = ListingStatus.Transacting;
                }
            }

            // 6. Create or renew the single bundle escrow
            string paymentReference;
            EscrowTransaction escrow;

            if (isRenew)
            {
                escrow = existingBundleEscrow!;
                escrow.UnlockAt = unlockAt;
                escrow.OriginalTicketPrice = totalOriginalPrice;
                escrow.BuyerFee = totalBuyerFee;
                escrow.SellerFee = totalSellerFee;
                escrow.TotalBuyerPaid = totalBuyerPaid;
                escrow.NetSellerPayout = totalNetSellerPayout;
                if (!string.IsNullOrWhiteSpace(request.RecipientName)) escrow.RecipientName = request.RecipientName;
                if (!string.IsNullOrWhiteSpace(request.RecipientEmail)) escrow.RecipientEmail = request.RecipientEmail;
                if (!string.IsNullOrWhiteSpace(request.RecipientIdCard)) escrow.RecipientIdCard = request.RecipientIdCard;
                paymentReference = escrow.PaymentReference!;

                var oldEscrowListings = await _dbContext.EscrowTransactionListings
                    .Where(etl => etl.EscrowId == escrow.Id)
                    .ToListAsync(cancellationToken);
                _dbContext.EscrowTransactionListings.RemoveRange(oldEscrowListings);

                foreach (var selected in selectedListings)
                {
                    await _dbContext.EscrowTransactionListings.AddAsync(new EscrowTransactionListing
                    {
                        Id = Guid.NewGuid(),
                        EscrowId = escrow.Id,
                        ListingId = selected.Id
                    }, cancellationToken);
                }
            }
            else
            {
                paymentReference = await GenerateUniquePaymentReferenceAsync(cancellationToken);
                escrow = new EscrowTransaction
                {
                    Id = Guid.NewGuid(),
                    ListingId = anchorListing.Id,
                    BundleId = bundleId,
                    BuyerId = buyerId,
                    SellerId = anchorListing.SellerId,
                    OriginalTicketPrice = totalOriginalPrice,
                    BuyerFee = totalBuyerFee,
                    SellerFee = totalSellerFee,
                    TotalBuyerPaid = totalBuyerPaid,
                    NetSellerPayout = totalNetSellerPayout,
                    PaymentReference = paymentReference,
                    Status = EscrowStatus.Pending,
                    UnlockAt = unlockAt,
                    RecipientName = !string.IsNullOrWhiteSpace(request.RecipientName) ? request.RecipientName : buyer.FullName,
                    RecipientEmail = !string.IsNullOrWhiteSpace(request.RecipientEmail) ? request.RecipientEmail : buyer.Email,
                    RecipientIdCard = request.RecipientIdCard
                };
                await _dbContext.EscrowTransactions.AddAsync(escrow, cancellationToken);

                foreach (var selected in selectedListings)
                {
                    await _dbContext.EscrowTransactionListings.AddAsync(new EscrowTransactionListing
                    {
                        Id = Guid.NewGuid(),
                        EscrowId = escrow.Id,
                        ListingId = selected.Id
                    }, cancellationToken);
                }
            }

            // 7. Generate VietQR for the aggregated total
            var vietQrResult = _vietQrService?.GenerateSystemQuickLink(totalBuyerPaid, paymentReference);

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (bundleTx != null)
            {
                await bundleTx.CommitAsync(cancellationToken);
            }

            // 8. Schedule hold expiry
            if (_messageSchedulerService != null)
            {
                await _messageSchedulerService.ScheduleHoldExpiryAsync(escrow.Id, anchorListing.Id, unlockAt, cancellationToken);
            }
            if (_paymentNotifier != null) await _paymentNotifier.NotifyMarketplaceListingChangedAsync(new { bundleId }, cancellationToken);

            // 9. Build response
            var response = new HoldListingForPurchaseResponse
            {
                EscrowId = escrow.Id,
                ListingId = anchorListing.Id,
                ListingStatus = ListingStatus.Transacting.ToString(),
                PaymentReference = paymentReference,
                QrImageUrl = vietQrResult?.QrImageUrl ?? string.Empty,
                QuickLinkUrl = vietQrResult?.QuickLinkUrl ?? string.Empty,
                BankBin = vietQrResult?.BankBin ?? string.Empty,
                AccountNumber = vietQrResult?.AccountNumber ?? string.Empty,
                AccountName = vietQrResult?.AccountName ?? string.Empty,
                ResalePrice = totalOriginalPrice,
                BuyerFee = totalBuyerFee,
                SellerFee = totalSellerFee,
                TotalBuyerPaid = totalBuyerPaid,
                NetSellerPayout = totalNetSellerPayout,
                UnlockAt = unlockAt,
                HoldDurationSeconds = (int)Math.Max(0, (unlockAt - DateTimeOffset.UtcNow).TotalSeconds),
                ServerNow = now,
                BundleId = bundleId,
                BundleTotalTickets = selectedListings.Count,
                BundleItems = bundleItems
            };

            return ApiResponse<HoldListingForPurchaseResponse>.SuccessResponse(
                response,
                $"Giữ chỗ {(selectedListings.Count > 1 ? $"gói {selectedListings.Count} vé" : "vé")} thành công! Vui lòng thanh toán trong vòng 10 phút.");
        }
        catch
        {
            if (bundleTx != null)
            {
                await bundleTx.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    private static long ComputeBundleLockKey(Guid bundleId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ts:hold:bundle:" + bundleId));
        return BitConverter.ToInt64(hash, 0);
    }

    private async Task EnsureUserExistsInShadowUsersAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) return;
        var exists = await _dbContext.ShadowUsers.AnyAsync(u => u.Id == userId, cancellationToken);
        if (!exists)
        {
            var userStr = userId.ToString();
            var shortId = userStr.Length >= 8 ? userStr[..8] : userStr;
            var shadowUser = new ShadowUser
            {
                Id = userId,
                Email = $"seller_{shortId}@ticketshield.vn",
                FullName = "Người bán vé " + shortId,
                Role = UserRole.User,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.ShadowUsers.AddAsync(shadowUser, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

