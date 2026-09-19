using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetPaymentStatus;

public sealed class GetPaymentStatusQueryHandler : IRequestHandler<GetPaymentStatusQuery, ApiResponse<GetPaymentStatusDto>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ICurrentUserService? _currentUserService;

    public GetPaymentStatusQueryHandler(
        ITicketShieldDbContext dbContext,
        ICurrentUserService? currentUserService = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<GetPaymentStatusDto>> Handle(GetPaymentStatusQuery request, CancellationToken cancellationToken)
    {
        var buyerId = _currentUserService?.UserId ?? Guid.Empty;
        if (buyerId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để xem trạng thái thanh toán.");
        }

        var listing = await _dbContext.ResaleListings
            .AsNoTracking()
            .Include(l => l.EscrowTransactions)
            .FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken);

        if (listing == null)
        {
            throw new NotFoundException("Tin đăng bán vé", request.ListingId);
        }

        var escrow = listing.EscrowTransactions
            .Where(e => e.BuyerId == buyerId)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefault();

        if (escrow == null)
        {
            if (listing.EscrowTransactions.Any())
            {
                throw new ForbiddenAccessException("Bạn không có quyền xem trạng thái thanh toán của giao dịch này.");
            }
            throw new NotFoundException("Giao dịch ký quỹ", request.ListingId);
        }

        var dto = new GetPaymentStatusDto
        {
            ListingId = listing.Id,
            EscrowId = escrow.Id,
            ListingStatus = listing.ListingStatus.ToString(),
            EscrowStatus = escrow.Status.ToString(),
            PaymentReference = escrow.PaymentReference,
            UnlockAt = escrow.UnlockAt,
            InSettlementBuffer = escrow.InSettlementBuffer
        };

        return ApiResponse<GetPaymentStatusDto>.SuccessResponse(dto, "Lấy trạng thái thanh toán thành công.");
    }
}
