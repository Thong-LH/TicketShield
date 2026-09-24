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
            if (escrow.Listing.ListingStatus == ListingStatus.Transacting)
            {
                var pastResaleCutoff = escrow.Listing.Event != null &&
                    escrow.Listing.Event.EventStartAt.AddHours(-2) <= now;
                escrow.Listing.ListingStatus = pastResaleCutoff
                    ? ListingStatus.Expired
                    : ListingStatus.Verified;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger?.LogWarning(
                "Late SePay payment queued for refund. EscrowId={EscrowId} BankTx={BankTx} PaymentRef={PaymentRef}",
                escrow.Id, bankTxRef, paymentReference);
            await TrySendLatePaymentEmailAsync(escrow, payload.TransferAmount, cancellationToken);

            if (_paymentNotifier != null)
            {
                await _paymentNotifier.NotifyHoldExpiredAsync(escrow.ListingId, new
                {
                    listingId = escrow.ListingId,
                    escrowId = escrow.Id,
                    paymentReference = escrow.PaymentReference ?? paymentReference,
                    escrowStatus = nameof(EscrowStatus.RefundQueued),
                    listingStatus = escrow.Listing.ListingStatus.ToString()
                }, cancellationToken);
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

        string? newTicketCode = null;
        string? qrCodeData = null;

        // Call gRPC TransferOwnership to BTC Organizer (Issue 2)
        if (_ticketVerificationService != null)
        {
            var buyerName = escrow.RecipientName ?? escrow.Buyer?.FullName ?? "Buyer";
            var buyerEmail = escrow.RecipientEmail ?? escrow.Buyer?.Email ?? "buyer@ticketshield.vn";
            var buyerPhone = escrow.Buyer?.PhoneNumber;

            var transferResponse = await _ticketVerificationService.TransferOwnershipByListingId(
                escrow.ListingId,
                escrow.BuyerId,
                buyerEmail,
                buyerName,
                buyerPhone,
                cancellationToken);

            newTicketCode = transferResponse.NewTicket?.Ticket?.TicketCode;
            qrCodeData = transferResponse.NewTicket?.Ticket?.TicketCode;

            if (newTicketCode is null)
            {
                throw new BusinessRuleViolationException(
                    "Không nhận được mã vé mới từ BTC Organizer sau khi chuyển quyền sở hữu. Giao dịch bị huỷ để bảo vệ Buyer.");
            }
        }

        escrow.Status = EscrowStatus.Locked;
        escrow.BankTransactionReference = bankTxRef;
        escrow.InSettlementBuffer = true;
        escrow.UnlockAt = EscrowTransaction.ComputeSettlementUnlockAt(now, eventStartAt);
        escrow.NewTicketCode = newTicketCode;
        escrow.QrCodeData = qrCodeData;
        escrow.Listing.ListingStatus = ListingStatus.Sold;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await TrySendLockEmailsAsync(escrow, cancellationToken);

        if (_paymentNotifier != null)
        {
            var notificationPayload = new
            {
                listingId = escrow.ListingId,
                escrowId = escrow.Id,
                paymentReference = escrow.PaymentReference ?? paymentReference,
                escrowStatus = escrow.Status.ToString(),
                listingStatus = escrow.Listing?.ListingStatus.ToString() ?? ListingStatus.Sold.ToString(),
                totalBuyerPaid = escrow.TotalBuyerPaid,
                newTicketCode = escrow.NewTicketCode,
                qrCodeData = escrow.QrCodeData
            };
            await _paymentNotifier.NotifyPaymentApprovedAsync(escrow.ListingId, notificationPayload, cancellationToken);
            await _paymentNotifier.NotifyOrderSettledAsync(escrow.ListingId, notificationPayload, cancellationToken);
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

    private async Task TrySendLockEmailsAsync(EscrowTransaction escrow, CancellationToken cancellationToken)
    {
        if (_emailService is null || _emailTemplates is null)
        {
            return;
        }

        try
        {
            var ev = escrow.Listing.Event;

            // Build a publicly-loadable QR image URL for the email.
            // Gmail blocks inline base64 images; qrserver.com returns a PNG via HTTPS that all clients can display.
            var qrPayloadForEmail = escrow.NewTicketCode ?? string.Empty;
            var qrImageUrlForEmail = string.IsNullOrWhiteSpace(qrPayloadForEmail)
                ? string.Empty
                : $"https://api.qrserver.com/v1/create-qr-code/?size=200x200&data={Uri.EscapeDataString(qrPayloadForEmail)}";

            var buyerHtml = _emailTemplates.GetBuyerTicketIssuedEmailHtml(
                escrow.Buyer.FullName,
                ev.Name,
                ev.EventStartAt.ToString("dd/MM/yyyy HH:mm"),
                ev.Venue,
                escrow.Listing.Tier?.TierName ?? string.Empty,
                string.Empty,
                escrow.NewTicketCode ?? string.Empty,
                qrImageUrlForEmail,
                escrow.PaymentReference ?? string.Empty,
                escrow.TotalBuyerPaid);
            var buyerTo = string.IsNullOrWhiteSpace(escrow.RecipientEmail)
                ? escrow.Buyer.Email
                : escrow.RecipientEmail;
            await _emailService.SendEmailAsync(
                buyerTo,
                "TicketShield — Xác nhận thanh toán vé",
                buyerHtml,
                cancellationToken);

            var sellerHtml = _emailTemplates.GetSellerEscrowLockedEmailHtml(
                escrow.Seller.FullName,
                escrow.Buyer.FullName,
                escrow.Listing.OriginalTicketCode,
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
