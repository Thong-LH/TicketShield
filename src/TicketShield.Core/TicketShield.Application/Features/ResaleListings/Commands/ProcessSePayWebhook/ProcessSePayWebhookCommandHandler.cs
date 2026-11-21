using System.Text.RegularExpressions;
using Grpc.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EscrowBuffer.Models;
using TicketShield.Contracts.Organizer.V1;
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
    private readonly IEscrowBufferSettings? _bufferSettings;

    public ProcessSePayWebhookCommandHandler(
        ITicketShieldDbContext dbContext,
        IEmailTemplateService? emailTemplates = null,
        IEmailService? emailService = null,
        ILogger<ProcessSePayWebhookCommandHandler>? logger = null,
        ITicketVerificationService? ticketVerificationService = null,
        IPaymentRealtimeNotifier? paymentNotifier = null,
        IEscrowBufferSettings? bufferSettings = null)
    {
        _dbContext = dbContext;
        _emailTemplates = emailTemplates;
        _emailService = emailService;
        _logger = logger;
        _ticketVerificationService = ticketVerificationService;
        _paymentNotifier = paymentNotifier;
        _bufferSettings = bufferSettings;
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
                "Webhook received but ignored because it is not an incoming payment transaction (transferType != 'in').");
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
            throw new BusinessRuleViolationException("Unable to find a valid payment reference (format TSXXXXXXXX) in transaction content.");
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

        if (escrow?.Listing != null && escrow.Listing.TicketId != Guid.Empty && escrow.Listing.Ticket == null)
        {
            escrow.Listing.Ticket = await _dbContext.Tickets
                .FirstOrDefaultAsync(t => t.Id == escrow.Listing.TicketId, cancellationToken);
        }

        if (escrow == null)
        {
            throw new NotFoundException("Escrow transaction", paymentReference);
        }

        var bankTxRef = string.IsNullOrWhiteSpace(payload.ReferenceCode)
            ? payload.Id.ToString()
            : payload.ReferenceCode;

        // 4. BR-E02: Payment Idempotency Check
        if ((!string.IsNullOrWhiteSpace(escrow.BankTransactionReference) &&
             string.Equals(escrow.BankTransactionReference, bankTxRef, StringComparison.OrdinalIgnoreCase)) ||
            escrow.Status == EscrowStatus.Locked ||
            escrow.Status == EscrowStatus.RefundQueued ||
            escrow.Status == EscrowStatus.TransferNeedsReview)
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
                "This payment transaction has already been processed (Payment Idempotency).");
        }

        // 5. Validate Listing & Escrow status.
        // Cancelled holds still accept a late SePay credit so money is queued for refund
        // instead of throwing (buyer may transfer after clicking cancel).
        if (escrow.Status != EscrowStatus.Pending && escrow.Status != EscrowStatus.Cancelled)
        {
            throw new BusinessRuleViolationException($"Escrow transaction is currently in '{escrow.Status}' status and cannot be locked.");
        }

        // 6. Validate Payment Amount
        if (payload.TransferAmount < escrow.TotalBuyerPaid)
        {
            throw new BusinessRuleViolationException(
                $"Payment amount ({payload.TransferAmount:N0} VNĐ) is less than total required amount ({escrow.TotalBuyerPaid:N0} VNĐ).");
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
                "Payment arrived after reservation hold expired. Transaction has been queued for refund.");
        }

        var issuedTickets = new List<IssuedTicket>();
        List<ResaleListing> listingsToTransfer;

        // Query EscrowTransactionListings to get the exact listings held for this escrow
        var escrowSelectedListingIds = await _dbContext.EscrowTransactionListings
            .Where(etl => etl.EscrowId == escrow.Id)
            .Select(etl => etl.ListingId)
            .ToListAsync(cancellationToken);

        if (escrowSelectedListingIds.Count > 0)
        {
            listingsToTransfer = await _dbContext.ResaleListings
                .Include(l => l.Event)
                .Where(l => escrowSelectedListingIds.Contains(l.Id))
                .OrderBy(l => l.CreatedAt)
                .ThenBy(l => l.Id)
                .ToListAsync(cancellationToken);
        }
        else if (escrow.BundleId.HasValue)
        {
            var bundleListings = await _dbContext.ResaleListings
                .Include(l => l.Event)
                .Where(l => l.BundleId == escrow.BundleId)
                .OrderBy(l => l.CreatedAt)
                .ThenBy(l => l.Id)
                .ToListAsync(cancellationToken);

            if (bundleListings.Count < 2 || bundleListings.Count > ResaleListing.MaxBundleTickets)
            {
                throw new BusinessRuleViolationException(
                    $"Invalid ticket bundle: requires 2 to {ResaleListing.MaxBundleTickets} actual tickets, but found {bundleListings.Count} tickets. Transaction rejected.");
            }

            listingsToTransfer = bundleListings;
        }
        else
        {
            listingsToTransfer = new List<ResaleListing> { escrow.Listing };
        }

        foreach (var l in listingsToTransfer)
        {
            if (l.Ticket == null && l.TicketId != Guid.Empty)
            {
                l.Ticket = await _dbContext.Tickets.FirstOrDefaultAsync(t => t.Id == l.TicketId, cancellationToken);
            }
        }

        // =========================================================================
        // PHA 1 (External Only): Gọi gRPC BTC Organizer hoàn toàn trong bộ nhớ.
        // Tuyệt đối KHÔNG mutate entity trong DbContext ChangeTracker ở pha này.
        // =========================================================================
        try
        {
            foreach (var listing in listingsToTransfer)
            {
                var issued = await TransferOneAtOrganizerAsync(listing, escrow, cancellationToken);
                issuedTickets.Add(issued);
            }
        }
        catch (Exception ex)
        {
            var isAmbiguousTimeout = IsAmbiguousTimeout(ex);

            if (isAmbiguousTimeout || issuedTickets.Count > 0)
            {
                _logger?.LogError(ex,
                    "Partial Transfer or Ambiguous Timeout for EscrowId {EscrowId}. TransferredCount={Count}. Moving to TransferNeedsReview (NO AUTO-REFUND).",
                    escrow.Id, issuedTickets.Count);

                escrow.Status = EscrowStatus.TransferNeedsReview;
                escrow.BankTransactionReference = bankTxRef;
                escrow.InSettlementBuffer = false;
                escrow.UnlockAt = null;
                if (issuedTickets.Count > 0)
                {
                    escrow.NewTicketCode = string.Join(",", issuedTickets.Select(t => t.NewCode));
                    escrow.QrCodeData = string.Join(",", issuedTickets.Select(t => t.QrCodeData));
                }

                // Vé đã sang tên thành công tại BTC Organizer tuyệt đối KHÔNG rollback về Verified.
                // Các vé còn lại giữ nguyên Transacting để bảo toàn trạng thái chờ đối soát.
                await _dbContext.SaveChangesAsync(cancellationToken);

                return ApiResponse<ProcessSePayWebhookResponse>.SuccessResponse(
                    new ProcessSePayWebhookResponse
                    {
                        EscrowId = escrow.Id,
                        ListingId = escrow.ListingId,
                        PaymentReference = escrow.PaymentReference ?? paymentReference,
                        EscrowStatus = nameof(EscrowStatus.TransferNeedsReview),
                        ListingStatus = escrow.Listing.ListingStatus.ToString(),
                        TransferAmount = payload.TransferAmount,
                        BankTransactionReference = bankTxRef,
                        IsIdempotentDuplicate = false
                    },
                    "Transaction requires review (TransferNeedsReview): Ticket transfer encountered a network error or partial transfer. Funds are held safely in escrow pending reconciliation.");
            }
            else
            {
                _logger?.LogError(ex,
                    "Saga Compensation Triggered: Transfer ownership failed for EscrowId {EscrowId} before any tickets were transferred. Rolling back listings to safe state and queueing full refund.",
                    escrow.Id);

                // Saga Compensation an toàn: 0 vé nào bị sang tên tại BTC Organizer
                foreach (var listing in listingsToTransfer)
                {
                    listing.ListingStatus = ListingStatus.Verified;
                }

                if (escrow.BundleId.HasValue)
                {
                    var otherBundleListings = await _dbContext.ResaleListings
                        .Where(l => l.BundleId == escrow.BundleId && l.ListingStatus == ListingStatus.Transacting)
                        .ToListAsync(cancellationToken);
                    foreach (var l in otherBundleListings)
                    {
                        l.ListingStatus = ListingStatus.Verified;
                    }
                }

                escrow.Status = EscrowStatus.RefundQueued;
                escrow.BankTransactionReference = bankTxRef;
                escrow.InSettlementBuffer = false;
                escrow.UnlockAt = null;

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
                    "Saga Compensation executed successfully: Ticket transfer failed; rolled back tickets and queued transaction for 100% refund.");
            }
        }

        // =========================================================================
        // PHA 2 (Persistence Only): Chỉ chạy khi 100% số vé trong danh sách đã cấp mã.
        // Lúc này mới cập nhật Entity, tạo Ticket mới cho Buyer và SaveChangesAsync.
        // =========================================================================
        var releasedSiblingListingIds = new List<Guid>();
        try
        {
            foreach (var listing in listingsToTransfer)
            {
                var issued = issuedTickets.First(t => t.ListingId == listing.Id);
                listing.ListingStatus = ListingStatus.Sold;

                if (listing.Ticket != null)
                {
                    listing.Ticket.Status = TicketStatus.Transferred;
                    listing.Ticket.UpdatedAt = now;
                }

                var buyerTicket = new Ticket
                {
                    Id = Guid.NewGuid(),
                    EventId = listing.EventId,
                    TierId = listing.TierId,
                    CurrentOwnerId = escrow.BuyerId,
                    TicketCode = issued.NewCode,
                    OriginalPrice = listing.OriginalPrice,
                    SeatZone = listing.SeatZone,
                    Status = TicketStatus.Valid,
                    PreviousTicketId = listing.TicketId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _dbContext.Tickets.AddAsync(buyerTicket, cancellationToken);
            }

            if (escrow.Listing != null)
            {
                escrow.Listing.ListingStatus = ListingStatus.Sold;
            }

            // Release remaining listings in the bundle that were held but not purchased
            if (escrow.BundleId.HasValue)
            {
                var transferredListingIds = listingsToTransfer.Select(l => l.Id).ToList();
                var remainingBundleListings = await _dbContext.ResaleListings
                    .Include(l => l.Event)
                    .Where(l => l.BundleId == escrow.BundleId && !transferredListingIds.Contains(l.Id))
                    .ToListAsync(cancellationToken);

                foreach (var remaining in remainingBundleListings)
                {
                    if (remaining.ListingStatus == ListingStatus.Transacting)
                    {
                        var pastResaleCutoff = remaining.Event != null &&
                            remaining.Event.EventStartAt.AddHours(-2) <= now;
                        remaining.ListingStatus = pastResaleCutoff ? ListingStatus.Expired : ListingStatus.Verified;
                        releasedSiblingListingIds.Add(remaining.Id);
                    }
                }
            }

            escrow.Status = EscrowStatus.Locked;
            escrow.BankTransactionReference = bankTxRef;
            escrow.InSettlementBuffer = true;
            var clocks = _bufferSettings == null
                ? new EscrowBufferConfig()
                : await _bufferSettings.GetAsync(cancellationToken);
            escrow.UnlockAt = EscrowTransaction.ComputeSettlementUnlockAt(
                now,
                TimeSpan.FromSeconds(clocks.BufferSeconds));
            escrow.TransferredAt = now;
            escrow.NewTicketCode = string.Join(",", issuedTickets.Select(t => t.NewCode));
            escrow.QrCodeData = string.Join(",", issuedTickets.Select(t => t.QrCodeData));

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex,
                "Phase 2 Persistence Failed for EscrowId {EscrowId} after successful Organizer transfer. Setting TransferNeedsReview for recovery worker.",
                escrow.Id);

            escrow.Status = EscrowStatus.TransferNeedsReview;
            escrow.BankTransactionReference = bankTxRef;
            escrow.InSettlementBuffer = false;
            escrow.UnlockAt = null;
            escrow.NewTicketCode = string.Join(",", issuedTickets.Select(t => t.NewCode));
            escrow.QrCodeData = string.Join(",", issuedTickets.Select(t => t.QrCodeData));

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ApiResponse<ProcessSePayWebhookResponse>.SuccessResponse(
                new ProcessSePayWebhookResponse
                {
                    EscrowId = escrow.Id,
                    ListingId = escrow.ListingId,
                    PaymentReference = escrow.PaymentReference ?? paymentReference,
                    EscrowStatus = nameof(EscrowStatus.TransferNeedsReview),
                    ListingStatus = escrow.Listing.ListingStatus.ToString(),
                    TransferAmount = payload.TransferAmount,
                    BankTransactionReference = bankTxRef,
                    IsIdempotentDuplicate = false
                },
                "Transaction requires review (TransferNeedsReview): Ticket transferred at Organizer but database save failed. Data preserved for recovery worker.");
        }
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
            await _paymentNotifier.NotifyMarketplaceListingChangedAsync(
                new { listingId = escrow.ListingId, bundleId = escrow.BundleId, listingStatus = ListingStatus.Sold.ToString() },
                cancellationToken);

            foreach (var releasedId in releasedSiblingListingIds)
            {
                await _paymentNotifier.NotifyMarketplaceListingChangedAsync(
                    new { listingId = releasedId, bundleId = escrow.BundleId, listingStatus = ListingStatus.Verified.ToString() },
                    cancellationToken);
            }
        }

        var response = new ProcessSePayWebhookResponse
        {
            EscrowId = escrow.Id,
            ListingId = escrow.ListingId,
            PaymentReference = escrow.PaymentReference ?? paymentReference,
            EscrowStatus = escrow.Status.ToString(),
            ListingStatus = ListingStatus.Sold.ToString(),
            TransferAmount = payload.TransferAmount,
            BankTransactionReference = bankTxRef,
            IsIdempotentDuplicate = false
        };

        return ApiResponse<ProcessSePayWebhookResponse>.SuccessResponse(
            response,
            "Payment webhook processed successfully! Escrow locked and ticket purchase confirmed.");
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
                        ? $"TicketShield — Ticket {issuedIndex + 1}/{issuedTickets.Count} issued"
                        : "TicketShield — Ticket Payment Confirmation",
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
                "TicketShield — Escrow Funds Secured",
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
                "TicketShield — Late payment received, refund in progress",
                $"<p>Hello {escrow.Buyer.FullName},</p><p>Your payment of {transferAmount:N0} VNĐ was received after the 10-minute hold window expired. Transaction {escrow.PaymentReference} has been queued for a full refund. The ticket has been returned to the marketplace.</p>",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to send late-payment email for EscrowId {EscrowId}", escrow.Id);
        }
    }

    private async Task<IssuedTicket> TransferOneAtOrganizerAsync(
        ResaleListing listing,
        EscrowTransaction escrow,
        CancellationToken cancellationToken)
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

            // Whitelist tightened (BE-12.1.3): Only Transferred or AlreadyTransferred are valid outcomes
            if (transferResponse != null &&
                (transferResponse.Outcome == TransferOutcome.Transferred ||
                 transferResponse.Outcome == TransferOutcome.AlreadyTransferred))
            {
                currentNewCode = transferResponse.NewTicket?.Ticket?.TicketCode;
                currentQrCode = transferResponse.NewTicket?.Ticket?.TicketCode;

                if (string.IsNullOrWhiteSpace(currentNewCode))
                {
                    throw new BusinessRuleViolationException(
                        $"Did not receive a new ticket code from Organizer for ticket {listing.OriginalTicketCode}. Transaction cancelled to protect buyer.");
                }
            }
            else
            {
                // US-12.2 (BE-12.2.2): BTC Organizer không xác nhận hoặc trả lỗi sang tên (kể cả Unspecified).
                // Triệt tiêu hoàn toàn hardcode string ATSH-GA-999 / ATSH-VIP-888.
                // TUYỆT ĐỐI KHÔNG fallback sang OriginalTicketCode cũ của Seller.
                // Kích hoạt Saga Compensation: rollback listing, hoàn tiền Buyer.
                throw new BusinessRuleViolationException(
                    $"Ticket transfer failed: Organizer did not confirm transfer session for ticket {listing.OriginalTicketCode}. Transaction cancelled to protect buyer.");
            }
        }
        else
        {
            // Khi _ticketVerificationService == null (môi trường test hoặc local fallback):
            // Sinh mã vé mới chuẩn hóa cấp cho Buyer, tuyệt đối KHÔNG cấp lại OriginalTicketCode cũ của Seller.
            currentNewCode = $"{listing.OriginalTicketCode}-TR-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
            currentQrCode = currentNewCode;
        }

        if (string.IsNullOrWhiteSpace(currentNewCode))
        {
            throw new BusinessRuleViolationException($"Cannot determine a valid ticket code for ticket {listing.OriginalTicketCode}.");
        }

        return new IssuedTicket(listing.Id, currentNewCode, currentQrCode ?? currentNewCode, listing.SeatZone);
    }

    private static bool IsAmbiguousTimeout(Exception ex)
    {
        if (ex is RpcException rpcEx &&
            (rpcEx.StatusCode == StatusCode.DeadlineExceeded ||
             rpcEx.StatusCode == StatusCode.Cancelled))
        {
            return true;
        }

        if (ex is OperationCanceledException or TimeoutException)
        {
            return true;
        }

        if (ex.InnerException != null)
        {
            return IsAmbiguousTimeout(ex.InnerException);
        }

        return false;
    }
}
