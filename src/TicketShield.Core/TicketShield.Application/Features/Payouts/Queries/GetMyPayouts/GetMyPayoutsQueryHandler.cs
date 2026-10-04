using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Payouts.Queries.GetMyPayouts;

public class GetMyPayoutsQueryHandler : IRequestHandler<GetMyPayoutsQuery, ApiResponse<List<MyPayoutDto>>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ICurrentUserService? _currentUserService;

    public GetMyPayoutsQueryHandler(ITicketShieldDbContext dbContext, ICurrentUserService? currentUserService = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<List<MyPayoutDto>>> Handle(GetMyPayoutsQuery request, CancellationToken cancellationToken)
    {
        var sellerId = _currentUserService?.UserId ?? Guid.Empty;
        if (sellerId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để xem lịch sử tiền bán vé.");
        }

        var payouts = await _dbContext.PayoutTransactions
            .AsNoTracking()
            .Where(payout => payout.SellerId == sellerId)
            .OrderByDescending(payout => payout.CreatedAt)
            .Select(payout => new MyPayoutDto
            {
                PayoutId = payout.Id,
                PayoutCode = payout.PayoutCode,
                EscrowId = payout.EscrowId,
                EscrowStatus = payout.Escrow.Status.ToString(),
                UnlockAt = payout.Escrow.UnlockAt,
                EventName = payout.Escrow.Listing.Event.Name,
                OriginalTicketCode = payout.Escrow.Listing.OriginalTicketCode,
                Amount = payout.Amount,
                Status = payout.Status.ToString(),
                RecipientBankCode = payout.RecipientBankCode,
                RecipientAccountNumber = payout.RecipientAccountNumber,
                RecipientAccountName = payout.RecipientAccountName,
                BankReferenceCode = payout.BankReferenceCode,
                RetryCount = payout.RetryCount,
                LastErrorMessage = payout.LastErrorMessage,
                ProcessedAt = payout.ProcessedAt,
                CreatedAt = payout.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<List<MyPayoutDto>>.SuccessResponse(payouts, "Lấy lịch sử tiền bán vé thành công.");
    }
}
