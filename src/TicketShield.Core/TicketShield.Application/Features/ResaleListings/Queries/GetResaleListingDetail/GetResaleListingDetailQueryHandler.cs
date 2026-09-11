using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Common;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;

public class GetResaleListingDetailQueryHandler : IRequestHandler<GetResaleListingDetailQuery, ApiResponse<ResaleListingDetailDto>>
{
    private readonly ITicketShieldDbContext _dbContext;

    public GetResaleListingDetailQueryHandler(ITicketShieldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<ResaleListingDetailDto>> Handle(GetResaleListingDetailQuery request, CancellationToken cancellationToken)
    {
        var listing = await _dbContext.ResaleListings
            .AsNoTracking()
            .Include(l => l.Event)
            .Include(l => l.Tier)
            .Include(l => l.Seller)
            .FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken);

        if (listing == null)
        {
            throw new NotFoundException("Tin bán vé", request.ListingId);
        }

        // Validate Private Access Token
        if (listing.IsPrivate)
        {
            if (string.IsNullOrWhiteSpace(request.Token) ||
                !string.Equals(listing.PrivateAccessToken, request.Token.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new ForbiddenAccessException("Bạn không có quyền truy cập vé riêng tư này. Vui lòng kiểm tra lại liên kết bảo mật hoặc yêu cầu người bán cấp liên kết mới.");
            }
        }

        return ApiResponse<ResaleListingDetailDto>.SuccessResponse(listing.ToDetailDto(), "Lấy thông tin vé thành công.");
    }
}
