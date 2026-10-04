using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.ProcessTicketUsedWebhook;

public class ProcessTicketUsedWebhookCommandHandler : IRequestHandler<ProcessTicketUsedWebhookCommand, ApiResponse<ProcessTicketUsedWebhookResponse>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly IEscrowPayoutSettler _settler;
    private readonly IPaymentRealtimeNotifier? _paymentNotifier;

    public ProcessTicketUsedWebhookCommandHandler(
        ITicketShieldDbContext dbContext,
        IEscrowPayoutSettler settler,
        IPaymentRealtimeNotifier? paymentNotifier = null)
    {
        _dbContext = dbContext;
        _settler = settler;
        _paymentNotifier = paymentNotifier;
    }

    public async Task<ApiResponse<ProcessTicketUsedWebhookResponse>> Handle(ProcessTicketUsedWebhookCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Payload.TicketCode))
        {
            throw new BusinessRuleViolationException("TicketCode không được để trống.");
        }

        var ticketCode = request.Payload.TicketCode.Trim();
        var escrow = await FindEscrowAsync(ticketCode, cancellationToken);
        if (escrow == null)
        {
            throw new NotFoundException("Giao dịch ký quỹ chứa vé", ticketCode);
        }

        if (escrow.Status == EscrowStatus.Locked)
        {
            await _settler.TrySettleAsync(escrow.Id, cancellationToken);
            var status = await _dbContext.EscrowTransactions.AsNoTracking()
                .Where(row => row.Id == escrow.Id)
                .Select(row => row.Status)
                .SingleAsync(cancellationToken);

            if (_paymentNotifier != null)
            {
                var notificationPayload = new
                {
                    listingId = escrow.ListingId,
                    escrowId = escrow.Id,
                    escrowStatus = status.ToString(),
                    ticketUsed = true
                };
                await _paymentNotifier.NotifyOrderSettledAsync(escrow.ListingId, notificationPayload, cancellationToken);
            }

            return ApiResponse<ProcessTicketUsedWebhookResponse>.SuccessResponse(
                new ProcessTicketUsedWebhookResponse
                {
                    EscrowId = escrow.Id,
                    EscrowStatus = status.ToString()
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

    private async Task<EscrowTransaction?> FindEscrowAsync(string ticketCode, CancellationToken cancellationToken)
    {
        var byNewCode = await _dbContext.EscrowTransactions.AsNoTracking()
            .FirstOrDefaultAsync(row => row.NewTicketCode != null && row.NewTicketCode.Contains(ticketCode), cancellationToken);
        if (byNewCode != null)
        {
            return byNewCode;
        }

        return await _dbContext.EscrowTransactions.AsNoTracking()
            .Include(row => row.Listing)
            .FirstOrDefaultAsync(row => row.Listing.OriginalTicketCode == ticketCode, cancellationToken);
    }
}
