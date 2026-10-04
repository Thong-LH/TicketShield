using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;

public class ProcessSePayWebhookCommandHandler : IRequestHandler<ProcessSePayWebhookCommand, ApiResponse<ProcessSePayWebhookResponse>>
{
    private static readonly Regex PaymentRefRegex = new(@"TS[A-Z0-9]{8}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ITicketVerificationService? _ticketVerificationService;
    private readonly IEmailTemplateService? _emailTemplates;
    private readonly IEmailService? _emailService;
    private readonly ILogger<ProcessSePayWebhookCommandHandler>? _logger;
    private readonly IPaymentRealtimeNotifier? _paymentNotifier;

    public ProcessSePayWebhookCommandHandler(
        ITicketShieldDbContext dbContext,
        IEmailTemplateService? emailTemplates = null,
        IEmailService? emailService = null,
        ILogger<ProcessSePayWebhookCommandHandler>? logger = null,
        ITicketVerificationService? ticketVerificationService = null,
        IPaymentRealtimeNotifier? paymentNotifier = null)
    {
        _dbContext = dbContext;
        _emailTemplates = emailTemplates;
        _emailService = emailService;
        _logger = logger;
        _ticketVerificationService = ticketVerificationService;
        _paymentNotifier = paymentNotifier;
    }

    public async Task<ApiResponse<ProcessSePayWebhookResponse>> Handle(ProcessSePayWebhookCommand request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;

        // 1. Check inbound transfer: If transferType is specified, must be 'in'; or if transferAmount > 0
        var isTransferIn = string.Equals(payload.TransferType, "in", StringComparison.OrdinalIgnoreCase) || 
                           string.IsNullOrWhiteSpace(payload.TransferType) && payload.TransferAmount > 0;

        if (!isTransferIn)
        {
            return ApiResponse<ProcessSePayWebhookResponse>.SuccessResponse(
                new ProcessSePayWebhookResponse
                {
                    IsIdempotentDuplicate = false,
                    EscrowStatus = "Ignored",
                    ListingStatus = "Ignored"
                },
                "Đã ghi nhận Webhook nhưng bỏ qua do không phải giao dịch tiền vào (transferType != 'in').");
        }

        // 2. Extract PaymentReference (Format: TS + 8 alphanumeric = 10 chars)
        string? paymentReference = null;
        if (!string.IsNullOrWhiteSpace(payload.Content))
        {
            var match = PaymentRefRegex.Match(payload.Content);
            if (match.Success)
            {
                paymentReference = match.Value.ToUpperInvariant();
            }
        }

        if (string.IsNullOrWhiteSpace(paymentReference) && !string.IsNullOrWhiteSpace(payload.Code))
        {
            var match = PaymentRefRegex.Match(payload.Code);
            if (match.Success)
            {
                paymentReference = match.Value.ToUpperInvariant();
            }
            else if (payload.Code.StartsWith("TS", StringComparison.OrdinalIgnoreCase) && payload.Code.Length == 10)
            {
                paymentReference = payload.Code.ToUpperInvariant();
            }
        }

        if (string.IsNullOrWhiteSpace(paymentReference))
        {
            throw new BusinessRuleViolationException("Không thể tìm thấy mã thanh toán hợp lệ (dạng TSXXXXXXXX) trong nội dung chuyển khoản.");
        }

        // 3. Find EscrowTransaction by PaymentReference
        var escrow = await _dbContext.EscrowTransactions
            .Include(e => e.Listing)
                .ThenInclude(l => l.Event)
            .Include(e => e.Listing)
                .ThenInclude(l => l.Tier)
            .Include(e => e.Buyer)
            .Include(e => e.Seller)
            .FirstOrDefaultAsync(e => e.PaymentReference == paymentReference, cancellationToken);

        if (escrow == null)
        {
            throw new NotFoundException("Giao dịch ký quỹ", paymentReference);
        }

        var bankTxRef = string.IsNullOrWhiteSpace(payload.ReferenceCode)
            ? payload.Id.ToString()
            : payload.ReferenceCode;

        // 4. BR-E02: Payment Idempotency Check
        if ((!string.IsNullOrWhiteSpace(escrow.BankTransactionReference) &&
             string.Equals(escrow.BankTransactionReference, bankTxRef, StringComparison.OrdinalIgnoreCase)) ||
            escrow.Status == EscrowStatus.Locked ||
            escrow.Status == EscrowStatus.RefundQueued)
        {
            return ApiResponse<ProcessSePayWebhookResponse>.SuccessResponse(
                new ProcessSePayWebhookResponse
                {
                    EscrowId = escrow.Id,
                    ListingId = escrow.ListingId,
                    PaymentReference = escrow.PaymentReference ?? paymentReference,
                    EscrowStatus = escrow.Status.ToString(),
                    ListingStatus = escrow.Listing.ListingStatus.ToString(),
                    TransferAmount = payload.TransferAmount,
                    BankTransactionReference = bankTxRef,
                    IsIdempotentDuplicate = true
                },
                "Giao dịch thanh toán này đã được xử lý trước đó (Payment Idempotency).");
        }

        // 5. Validate Listing & Escrow status.
        // Cancelled holds still accept a late SePay credit so money is queued for refund
        // instead of throwing (buyer may transfer after clicking cancel).
        if (escrow.Status != EscrowStatus.Pending && escrow.Status != EscrowStatus.Cancelled)
        {
            throw new BusinessRuleViolationException($"Giao dịch ký quỹ đang ở trạng thái '{escrow.Status}' và không thể khóa.");
        }

        // 6. Validate Payment Amount
        if (payload.TransferAmount < escrow.TotalBuyerPaid)
        {
            throw new BusinessRuleViolationException(
                $"Số tiền thanh toán ({payload.TransferAmount:N0} VNĐ) nhỏ hơn tổng số tiền cần trả ({escrow.TotalBuyerPaid:N0} VNĐ).");
        }

        // 7. Lock Escrow while THIS hold window is still open.
        // Do not require listing.Transacting: ExpiredHoldReleaseWorker can flip the listing
        // to Verified because an older Pending escrow expired, while this escrow is still live.
        var now = DateTimeOffset.UtcNow;
        var listingUnavailable =
            escrow.Listing.ListingStatus == ListingStatus.Sold ||
            escrow.Listing.ListingStatus == ListingStatus.Cancelled ||
            escrow.Listing.ListingStatus == ListingStatus.Expired;
        var holdStillValid =
            escrow.Status == EscrowStatus.Pending &&
            !listingUnavailable &&
            escrow.UnlockAt.HasValue &&
            escrow.UnlockAt.Value > now;

        if (!holdStillValid)
        {
            escrow.Status = EscrowStatus.RefundQueued;
            escrow.BankTransactionReference = bankTxRef;
            escrow.InSettlementBuffer = false;
            var pastResaleCutoff = escrow.Listing.Event != null &&
                escrow.Listing.Event.EventStartAt.AddHours(-2) <= now;
            var newStatus = pastResaleCutoff ? ListingStatus.Expired : ListingStatus.Verified;

            var affectedListingIds = new List<Guid> { escrow.ListingId };

            if (escrow.BundleId.HasValue)
            {
                var bundleListings = await _dbContext.ResaleListings
                    .Where(l => l.BundleId == escrow.BundleId && l.ListingStatus == ListingStatus.Transacting)
                    .ToListAsync(cancellationToken);
                foreach (var l in bundleListings)
                {
                    l.ListingStatus = newStatus;
                    if (!affectedListingIds.Contains(l.Id)) affectedListingIds.Add(l.Id);
                }
            }
            else if (escrow.Listing.ListingStatus == ListingStatus.Transacting)
            {
                escrow.Listing.ListingStatus = newStatus;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger?.LogWarning(
                "Late SePay payment queued for refund. EscrowId={EscrowId} BankTx={BankTx} PaymentRef={PaymentRef}",
                escrow.Id, bankTxRef, paymentReference);
            await TrySendLatePaymentEmailAsync(escrow, payload.TransferAmount, cancellationToken);

            if (_paymentNotifier != null)
            {
                foreach (var lId in affectedListingIds)
                {
                    await _paymentNotifier.NotifyHoldExpiredAsync(lId, new
                    {
                        listingId = lId,
                        escrowId = escrow.Id,
                        paymentReference = escrow.PaymentReference ?? paymentReference,
                        escrowStatus = nameof(EscrowStatus.RefundQueued),
                        listingStatus = newStatus.ToString()
                    }, cancellationToken);
                }
            }

            return ApiResponse<ProcessSePayWebhookResponse>.SuccessResponse(
                new ProcessSePayWebhookResponse
                {
                    EscrowId = escrow.Id,
                    ListingId = escrow.ListingId,
                    PaymentReference = escrow.PaymentReference ?? paymentReference,
                    EscrowStatus = nameof(EscrowStatus.RefundQueued),
                    ListingStatus = escrow.Listing.ListingStatus.ToString(),
                    TransferAmount = payload.TransferAmount,
                    BankTransactionReference = bankTxRef,
                    IsIdempotentDuplicate = false
                },
                "Thanh toán đến sau khi hết hạn giữ chỗ. Giao dịch đã đưa vào hàng đợi hoàn tiền.");
        }

        var eventStartAt = escrow.Listing.Event?.EventStartAt
            ?? throw new BusinessRuleViolationException("Thiếu EventStartAt để tính UnlockAt giải ngân.");

        var issuedTickets = new List<IssuedTicket>();

        var listingsToTransfer = new List<ResaleListing> { escrow.Listing };
        if (escrow.BundleId.HasValue)
        {
            var bundleListings = await _dbContext.ResaleListings
                .Where(l => l.BundleId == escrow.BundleId)
                .OrderBy(l => l.CreatedAt)
                .ThenBy(l => l.Id)
                .ToListAsync(cancellationToken);

            if (bundleListings.Count < 2 || bundleListings.Count > ResaleListing.MaxBundleTickets)
            {
                throw new BusinessRuleViolationException(
                    $"Gói vé không hợp lệ: cần từ 2 đến {ResaleListing.MaxBundleTickets} vé thật nhưng chỉ tìm thấy {bundleListings.Count} vé. Giao dịch bị từ chối.");
            }

            listingsToTransfer = bundleListings;
        }

        try
        {
            foreach (var listing in listingsToTransfer)
            {
                string? currentNewCode = null;
                string? currentQrCode = null;

                // Call gRPC TransferOwnership to BTC Organizer (Issue 2)
                if (_ticketVerificationService != null)
                {
                    var buyerName = escrow.RecipientName ?? escrow.Buyer?.FullName ?? "Buyer";
                    var buyerEmail = escrow.RecipientEmail ?? escrow.Buyer?.Email ?? "buyer@ticketshield.vn";
                    var buyerPhone = escrow.Buyer?.PhoneNumber;

                    var transferResponse = await _ticketVerificationService.TransferOwnershipByListingId(
                        listing.Id,
                        escrow.BuyerId,
                        buyerEmail,
                        buyerName,
                        buyerPhone,
                        cancellationToken);

                    if (transferResponse != null && transferResponse.Outcome != TicketShield.Contracts.Organizer.V1.TransferOutcome.Unspecified)
                    {
                        currentNewCode = transferResponse.NewTicket?.Ticket?.TicketCode;
                        currentQrCode = transferResponse.NewTicket?.Ticket?.TicketCode;

                        if (currentNewCode is null)
                        {
                            throw new BusinessRuleViolationException(
                                $"Không nhận được mã vé mới từ BTC Organizer cho vé {listing.OriginalTicketCode}. Giao dịch bị huỷ để bảo vệ Buyer.");
                        }
                    }
                }

                // Seeded marketplace listings (e.g. ATSH-GA-999) without an external BTC lock session
                if (string.IsNullOrWhiteSpace(currentNewCode))
                {
                    currentNewCode = listing.OriginalTicketCode;
                    currentQrCode = currentNewCode;
                }

                if (string.IsNullOrWhiteSpace(currentNewCode))
                {
                    throw new BusinessRuleViolationException($"Không thể xác định mã vé hợp lệ cho vé {listing.OriginalTicketCode}.");
                }

                issuedTickets.Add(new IssuedTicket(listing.Id, currentNewCode, currentQrCode ?? currentNewCode, listing.SeatZone));
                listing.ListingStatus = ListingStatus.Sold;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Saga Compensation Triggered: Transfer ownership failed for EscrowId {EscrowId}. Rolling back listings to safe state and queueing full refund.", escrow.Id);
            
            // Saga Compensation:
            // 1. Rollback all listings to safe state (Verified)
            foreach (var listing in listingsToTransfer)
            {
                listing.ListingStatus = ListingStatus.Verified;
            }
            
            // 2. Queue Full Refund for the buyer
            escrow.Status = EscrowStatus.RefundQueued;
            escrow.BankTransactionReference = bankTxRef;
            escrow.InSettlementBuffer = false;
            
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            return ApiResponse<ProcessSePayWebhookResponse>.SuccessResponse(
                new ProcessSePayWebhookResponse
                {
                    EscrowId = escrow.Id,
                    ListingId = escrow.ListingId,
                    PaymentReference = escrow.PaymentReference ?? paymentReference,
                    EscrowStatus = nameof(EscrowStatus.RefundQueued),
                    ListingStatus = ListingStatus.Verified.ToString(),
                    TransferAmount = payload.TransferAmount,
                    BankTransactionReference = bankTxRef,
                    IsIdempotentDuplicate = false
                },
                "Xử lý bù trừ (Saga Compensation) thành công: Chuyển tên vé thất bại, đã rollback vé và đưa giao dịch vào luồng hoàn tiền 100%.");
        }

        escrow.Status = EscrowStatus.Locked;
        escrow.BankTransactionReference = bankTxRef;
        escrow.InSettlementBuffer = true;
        escrow.UnlockAt = EscrowTransaction.ComputeSettlementUnlockAt(now, eventStartAt);
        escrow.NewTicketCode = string.Join(",", issuedTickets.Select(t => t.NewCode));
        escrow.QrCodeData = string.Join(",", issuedTickets.Select(t => t.QrCodeData));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await TrySendLockEmailsAsync(escrow, issuedTickets, cancellationToken);

        if (_paymentNotifier != null)
        {
            foreach (var listing in listingsToTransfer)
            {
                var issued = issuedTickets.First(t => t.ListingId == listing.Id);
                var notificationPayload = new
                {
                    listingId = listing.Id,
                    sellerId = listing.SellerId,
                    escrowId = escrow.Id,
                    paymentReference = escrow.PaymentReference ?? paymentReference,
                    escrowStatus = escrow.Status.ToString(),
                    listingStatus = listing.ListingStatus.ToString(),
                    totalBuyerPaid = escrow.TotalBuyerPaid,
                    newTicketCode = issued.NewCode,
                    qrCodeData = issued.QrCodeData
                };
                await _paymentNotifier.NotifyPaymentApprovedAsync(listing.Id, notificationPayload, cancellationToken);
                await _paymentNotifier.NotifyOrderSettledAsync(listing.Id, notificationPayload, cancellationToken);
            }
        }

        var response = new ProcessSePayWebhookResponse
        {
            EscrowId = escrow.Id,
            ListingId = escrow.ListingId,
            PaymentReference = escrow.PaymentReference ?? paymentReference,
            EscrowStatus = escrow.Status.ToString(),
            ListingStatus = escrow.Listing?.ListingStatus.ToString() ?? ListingStatus.Sold.ToString(),
            TransferAmount = payload.TransferAmount,
            BankTransactionReference = bankTxRef,
            IsIdempotentDuplicate = false
        };

        return ApiResponse<ProcessSePayWebhookResponse>.SuccessResponse(
            response,
            "Xử lý Webhook thanh toán thành công! Đã khóa Escrow và xác nhận bán vé.");
    }

    private async Task TrySendLockEmailsAsync(
        EscrowTransaction escrow,
        IReadOnlyList<IssuedTicket> issuedTickets,
        CancellationToken cancellationToken)
    {
        if (_emailService is null || _emailTemplates is null)
        {
            return;
        }

        try
        {
            var ev = escrow.Listing.Event;
            var buyerTo = string.IsNullOrWhiteSpace(escrow.RecipientEmail)
                ? escrow.Buyer.Email
                : escrow.RecipientEmail;

            // Gửi MỘT email cho MỖI vé thật: mỗi vé có mã và QR riêng, không gộp chuỗi.
            for (var issuedIndex = 0; issuedIndex < issuedTickets.Count; issuedIndex++)
            {
                var issued = issuedTickets[issuedIndex];

                // Gmail chặn ảnh base64 inline; qrserver.com trả về PNG qua HTTPS mọi client đều hiển thị được.
                var qrImageUrlForEmail = string.IsNullOrWhiteSpace(issued.QrCodeData)
                    ? string.Empty
                    : $"https://api.qrserver.com/v1/create-qr-code/?size=200x200&data={Uri.EscapeDataString(issued.QrCodeData)}";

                var buyerHtml = _emailTemplates.GetBuyerTicketIssuedEmailHtml(
                    escrow.Buyer.FullName,
                    ev.Name,
                    ev.EventStartAt.ToString("dd/MM/yyyy HH:mm"),
                    ev.Venue,
                    escrow.Listing.Tier?.TierName ?? string.Empty,
                    issued.SeatZone ?? string.Empty,
                    issued.NewCode,
                    qrImageUrlForEmail,
                    escrow.PaymentReference ?? string.Empty,
                    escrow.TotalBuyerPaid);

                await _emailService.SendEmailAsync(
                    buyerTo,
                    issuedTickets.Count > 1
                        ? $"TicketShield — Vé {issuedIndex + 1}/{issuedTickets.Count} đã được cấp"
                        : "TicketShield — Xác nhận thanh toán vé",
                    buyerHtml,
                    cancellationToken);
            }

            var originalTicketCodes = escrow.BundleId.HasValue
                ? string.Join(", ", issuedTickets.Select(t => t.NewCode))
                : escrow.Listing.OriginalTicketCode;

            var sellerHtml = _emailTemplates.GetSellerEscrowLockedEmailHtml(
                escrow.Seller.FullName,
                escrow.Buyer.FullName,
                originalTicketCodes,
                ev.Name,
                escrow.NetSellerPayout,
                escrow.PaymentReference ?? string.Empty);
            await _emailService.SendEmailAsync(
                escrow.Seller.Email,
                "TicketShield — Tiền đã được ký quỹ",
                sellerHtml,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to send escrow confirmation emails for EscrowId {EscrowId}", escrow.Id);
        }
    }

    private async Task TrySendLatePaymentEmailAsync(EscrowTransaction escrow, decimal transferAmount, CancellationToken cancellationToken)
    {
        if (_emailService is null)
        {
            return;
        }

        try
        {
            var buyerTo = string.IsNullOrWhiteSpace(escrow.RecipientEmail)
                ? escrow.Buyer.Email
                : escrow.RecipientEmail;
            await _emailService.SendEmailAsync(
                buyerTo,
                "TicketShield — Thanh toán đến muộn, đang hoàn tiền",
                $"<p>Xin chào {escrow.Buyer.FullName},</p><p>Tiền chuyển khoản {transferAmount:N0} VNĐ đến sau 10 phút giữ chỗ. Giao dịch {escrow.PaymentReference} đã đưa vào hàng đợi hoàn tiền. Vé đã được mở bán lại.</p>",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to send late-payment email for EscrowId {EscrowId}", escrow.Id);
        }
    }
}
