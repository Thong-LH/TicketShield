using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.ProcessTicketUsedWebhook;

public class ProcessTicketUsedWebhookCommandHandler : IRequestHandler<ProcessTicketUsedWebhookCommand, ApiResponse<ProcessTicketUsedWebhookResponse>>
{
    private readonly ITicketShieldDbContext _dbContext;
    // Add real-time notifier to notify clients if needed
    private readonly IPaymentRealtimeNotifier? _paymentNotifier;

    public ProcessTicketUsedWebhookCommandHandler(ITicketShieldDbContext dbContext, IPaymentRealtimeNotifier? paymentNotifier = null)
    {
        _dbContext = dbContext;
        _paymentNotifier = paymentNotifier;
    }

    public async Task<ApiResponse<ProcessTicketUsedWebhookResponse>> Handle(ProcessTicketUsedWebhookCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Payload.TicketCode))
        {
            throw new BusinessRuleViolationException("TicketCode không được để trống.");
        }

        var ticketCode = request.Payload.TicketCode.Trim();
        var escrow = await _dbContext.EscrowTransactions
            .FirstOrDefaultAsync(e => e.NewTicketCode != null && e.NewTicketCode.Contains(ticketCode), cancellationToken);

        if (escrow == null)
        {
            // Fallback: search by OriginalTicketCode (if not changed)
            escrow = await _dbContext.EscrowTransactions
                .Include(e => e.Listing)
                .FirstOrDefaultAsync(e => e.Listing.OriginalTicketCode == ticketCode, cancellationToken);
                
            if (escrow == null)
            {
                throw new NotFoundException("Giao dịch ký quỹ chứa vé", ticketCode);
            }
        }

        if (escrow.Status == EscrowStatus.Locked)
        {
            // Triggers AutomaticSettlementWorker to pick this up immediately
            escrow.UnlockAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            // Optionally notify client that the ticket was used
            if (_paymentNotifier != null)
            {
                // Similar to other notifications, maybe we just send an order update
                var notificationPayload = new
                {
                    listingId = escrow.ListingId,
                    escrowId = escrow.Id,
                    escrowStatus = escrow.Status.ToString(),
                    ticketUsed = true
                };
                await _paymentNotifier.NotifyOrderSettledAsync(escrow.ListingId, notificationPayload, cancellationToken);
            }

            return ApiResponse<ProcessTicketUsedWebhookResponse>.SuccessResponse(
                new ProcessTicketUsedWebhookResponse
                {
                    EscrowId = escrow.Id,
                    EscrowStatus = escrow.Status.ToString()
                },
                "Đã nhận webhook báo vé sử dụng. Kích hoạt giải ngân sớm thành công.");
        }

        return ApiResponse<ProcessTicketUsedWebhookResponse>.SuccessResponse(
            new ProcessTicketUsedWebhookResponse
            {
                EscrowId = escrow.Id,
                EscrowStatus = escrow.Status.ToString()
            },
            $"Webhook ghi nhận thành công, nhưng giao dịch ký quỹ hiện đang ở trạng thái {escrow.Status}.");
    }
}
