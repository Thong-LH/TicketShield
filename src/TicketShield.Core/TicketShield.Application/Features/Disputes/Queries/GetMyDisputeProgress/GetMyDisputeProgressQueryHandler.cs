using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Disputes.Queries.GetMyDisputeProgress;

public class GetMyDisputeProgressQueryHandler : IRequestHandler<GetMyDisputeProgressQuery, ApiResponse<MyDisputeProgressDto>>
{
    private readonly ITicketShieldDbContext _db;
    private readonly ICurrentUserService? _currentUser;

    public GetMyDisputeProgressQueryHandler(ITicketShieldDbContext db, ICurrentUserService? currentUser = null)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<MyDisputeProgressDto>> Handle(GetMyDisputeProgressQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser is not { IsAuthenticated: true, UserId: { } userId } || userId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để xem tiến độ khiếu nại.");
        }

        var dispute = await _db.Disputes
            .AsNoTracking()
            .Include(row => row.Escrow)
            .ThenInclude(row => row.PayoutTransaction)
            .FirstOrDefaultAsync(row => row.Id == request.DisputeId, cancellationToken);
        if (dispute == null)
        {
            throw new NotFoundException("Khiếu nại", request.DisputeId);
        }

        // Chỉ người trả tiền và người bán của giao dịch. Người nhận vé theo email không được xem.
        DisputeViewerRole viewer;
        if (dispute.BuyerId == userId)
        {
            viewer = DisputeViewerRole.Buyer;
        }
        else if (dispute.Escrow.SellerId == userId)
        {
            viewer = DisputeViewerRole.Seller;
        }
        else
        {
            throw new ForbiddenAccessException("Bạn không có quyền xem khiếu nại này.");
        }

        return ApiResponse<MyDisputeProgressDto>.SuccessResponse(MyDisputeProgressMapper.Build(dispute, viewer));
    }
}
