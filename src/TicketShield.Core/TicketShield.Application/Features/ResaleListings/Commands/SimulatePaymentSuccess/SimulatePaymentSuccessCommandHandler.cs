using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.SimulatePaymentSuccess;

public class SimulatePaymentSuccessCommandHandler : IRequestHandler<SimulatePaymentSuccessCommand, ApiResponse<ProcessSePayWebhookResponse>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly IMediator _mediator;

    public SimulatePaymentSuccessCommandHandler(ITicketShieldDbContext dbContext, IMediator mediator)
    {
        _dbContext = dbContext;
        _mediator = mediator;
    }

    public async Task<ApiResponse<ProcessSePayWebhookResponse>> Handle(SimulatePaymentSuccessCommand request, CancellationToken cancellationToken)
    {
        // FIX Lỗi 27: EscrowTransaction chỉ lưu ListingId của vé đại diện đầu tiên trong Combo.
        // Khi dev thử nghiệm bấm simulate cho vé con thứ 2/3/4, cần tìm theo BundleId.
        var escrow = await _dbContext.EscrowTransactions
            .AsNoTracking()
            .Where(e => e.Status == EscrowStatus.Pending &&
                        (e.ListingId == request.ListingId
                         || e.BundleId == _dbContext.ResaleListings
                             .Where(l => l.Id == request.ListingId)
                             .Select(l => (Guid?)l.BundleId)
                             .FirstOrDefault()))
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (escrow == null || string.IsNullOrWhiteSpace(escrow.PaymentReference))
        {
            throw new BadRequestException("Không tìm thấy phiên giữ chỗ đang chờ thanh toán cho vé này.");
        }

        var payload = new SePayWebhookRequest
        {
            Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Gateway = "DEV_MOCK_SEPAY",
            TransactionDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "TICKETSHIELD_ESCROW",
            Content = escrow.PaymentReference,
            TransferType = "in",
            TransferAmount = escrow.TotalBuyerPaid,
            Accumulated = escrow.TotalBuyerPaid,
            ReferenceCode = $"SIM_{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Description = $"Giả lập thanh toán thành công cho mã {escrow.PaymentReference}"
        };

        return await _mediator.Send(new ProcessSePayWebhookCommand(payload), cancellationToken);
    }
}
