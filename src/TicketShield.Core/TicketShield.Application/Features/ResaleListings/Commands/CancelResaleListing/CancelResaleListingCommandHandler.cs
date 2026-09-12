using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.CancelResaleListing;

public class CancelResaleListingCommandHandler : IRequestHandler<CancelResaleListingCommand, ApiResponse<CancelResaleListingResponse>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ICurrentUserService? _currentUserService;
    private readonly ITicketVerificationService? _verificationService;

    public CancelResaleListingCommandHandler(
        ITicketShieldDbContext dbContext,
        ICurrentUserService? currentUserService = null,
        ITicketVerificationService? verificationService = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _verificationService = verificationService;
    }

    public async Task<ApiResponse<CancelResaleListingResponse>> Handle(CancelResaleListingCommand request, CancellationToken cancellationToken)
    {
        // 1. Resolve seller ID from current authenticated user — no anonymous fallback
        var sellerId = _currentUserService?.UserId ?? Guid.Empty;
        if (sellerId == Guid.Empty)
            throw new UnauthorizedException("Bạn phải đăng nhập để hủy tin đăng bán vé.");

        // 2. Retrieve resale listing
        var listing = await _dbContext.ResaleListings
            .FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken);

        if (listing == null)
        {
            throw new NotFoundException("Tin đăng bán vé", request.ListingId);
        }

        // 3. Step 1 of Jira: Validate ownership (only the listing seller can cancel)
        if (listing.SellerId != sellerId)
        {
            throw new ForbiddenAccessException("Bạn không có quyền hủy tin đăng bán vé này.");
        }

        // 4. Step 1 of Jira: Check ticket is not bought yet (must be in Verified status, not Transacting or Sold)
        if (listing.ListingStatus != ListingStatus.Verified)
        {
            throw new BusinessRuleViolationException(
                $"Tin đăng bán vé hiện đang ở trạng thái '{listing.ListingStatus}'. Chỉ có thể hủy tin đăng khi vé chưa bị người mua đặt hoặc mua.");
        }

        // 5. Step 3 of Jira: Call MockOrganizer via verification service to unlock original ticket if gRPC session exists
        if (_verificationService != null)
        {
            var idempotencyKey = $"cancel-listing-{listing.Id}";
            // CancelByListingId looks up the verificationId via listing index in core_resale_records.
            // If no gRPC session exists (e.g. seeded data), CancelByListingId returns gracefully.
            // If an active session exists and gRPC unlock fails, this throws and aborts cancellation.
            await _verificationService.CancelByListingId(sellerId.ToString("D"), listing.Id, idempotencyKey, cancellationToken);
        }

        // 6. Step 2 of Jira: Change listing status to CANCELLED in DB and save
        listing.ListingStatus = ListingStatus.Cancelled;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new CancelResaleListingResponse
        {
            ListingId = listing.Id,
            OriginalTicketCode = listing.OriginalTicketCode,
            ListingStatus = listing.ListingStatus.ToString(),
            CancelledAt = DateTimeOffset.UtcNow
        };

        return ApiResponse<CancelResaleListingResponse>.SuccessResponse(response, "Hủy tin đăng bán vé thành công và đã gửi yêu cầu mở khóa vé.");
    }
}
