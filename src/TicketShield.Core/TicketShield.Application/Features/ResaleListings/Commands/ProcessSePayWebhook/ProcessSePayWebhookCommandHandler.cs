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
    private readonly ILogger<ProcessSePayWebhookCommandHandler>? _logger;

    public ProcessSePayWebhookCommandHandler(
        ITicketShieldDbContext dbContext,
        ILogger<ProcessSePayWebhookCommandHandler>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ApiResponse<ProcessSePayWebhookResponse>> Handle(ProcessSePayWebhookCommand request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;

        // 1. Ignore outbound transfer webhooks
        if (!string.Equals(payload.TransferType, "in", StringComparison.OrdinalIgnoreCase))
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

        // 5. Validate Listing & Escrow status
        if (escrow.Status != EscrowStatus.Pending)
        {
            throw new BusinessRuleViolationException($"Giao dịch ký quỹ đang ở trạng thái '{escrow.Status}' và không thể khóa.");
        }

        // 6. Validate Payment Amount
        if (payload.TransferAmount < escrow.TotalBuyerPaid)
        {
            throw new BusinessRuleViolationException(
                $"Số tiền thanh toán ({payload.TransferAmount:N0} VNĐ) nhỏ hơn tổng số tiền cần trả ({escrow.TotalBuyerPaid:N0} VNĐ).");
        }

        // 7. Lock Escrow and mark Listing as Sold only while the 10-minute hold is still valid
        var now = DateTimeOffset.UtcNow;
        var holdStillValid =
            escrow.Listing.ListingStatus == ListingStatus.Transacting &&
            escrow.UnlockAt.HasValue &&
            escrow.UnlockAt.Value > now;

        if (!holdStillValid)
        {
            escrow.Status = EscrowStatus.RefundQueued;
            escrow.BankTransactionReference = bankTxRef;
            escrow.InSettlementBuffer = false;
            if (escrow.Listing.ListingStatus == ListingStatus.Transacting)
            {
                escrow.Listing.ListingStatus = ListingStatus.Verified;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger?.LogWarning(
                "Late SePay payment queued for refund. EscrowId={EscrowId} BankTx={BankTx} PaymentRef={PaymentRef}",
                escrow.Id, bankTxRef, paymentReference);

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

        escrow.Status = EscrowStatus.Locked;
        escrow.BankTransactionReference = bankTxRef;
        escrow.InSettlementBuffer = true;
        escrow.UnlockAt = EscrowTransaction.ComputeSettlementUnlockAt(now, eventStartAt);
        escrow.Listing.ListingStatus = ListingStatus.Sold;

        await _dbContext.SaveChangesAsync(cancellationToken);

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
}
