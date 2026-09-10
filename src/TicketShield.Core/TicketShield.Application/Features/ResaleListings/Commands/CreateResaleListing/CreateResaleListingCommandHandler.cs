using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.CreateResaleListing;

public class CreateResaleListingCommandHandler : IRequestHandler<CreateResaleListingCommand, ApiResponse<CreateResaleListingResponse>>
{
    private readonly ITicketShieldDbContext _dbContext;

    public CreateResaleListingCommandHandler(ITicketShieldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<CreateResaleListingResponse>> Handle(CreateResaleListingCommand request, CancellationToken cancellationToken)
    {
        // 1. Resolve SellerId (fallback to first user if not supplied in dev mode)
        var sellerId = request.SellerId ?? Guid.Empty;
        if (sellerId == Guid.Empty)
        {
            var defaultSeller = await _dbContext.Users
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (defaultSeller == null)
            {
                throw new NotFoundException("Không tìm thấy thông tin người bán trong hệ thống.");
            }
            sellerId = defaultSeller.Id;
        }

        // 2. Validate Event and Tier existence
        var eventExists = await _dbContext.Events
            .AnyAsync(e => e.Id == request.EventId, cancellationToken);
        if (!eventExists)
        {
            throw new NotFoundException("Sự kiện", request.EventId);
        }

        var tierExists = await _dbContext.TicketTiers
            .AnyAsync(t => t.Id == request.TierId && t.EventId == request.EventId, cancellationToken);
        if (!tierExists)
        {
            throw new NotFoundException("Hạng vé", request.TierId);
        }

        // 3. Check for existing active listing for this ticket (Partial Unique Index safeguard)
        var hasActiveListing = await _dbContext.ResaleListings
            .AnyAsync(l => l.OriginalTicketCode == request.OriginalTicketCode &&
                           (l.ListingStatus == ListingStatus.Verified || l.ListingStatus == ListingStatus.Transacting),
                      cancellationToken);

        if (hasActiveListing)
        {
            throw new BusinessRuleViolationException(
                $"Vé '{request.OriginalTicketCode}' hiện đang được niêm yết bán trên hệ thống. Không thể tạo thêm tin bán.");
        }

        // 4. Generate Private Access Token if Private Mode selected
        string? privateAccessToken = null;
        string? shareUrl = null;
        if (request.IsPrivate)
        {
            // High-entropy 32-character hex token
            privateAccessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            shareUrl = $"https://ticketshield.vn/p/{privateAccessToken}";
        }

        // 5. Create Entity and enforce Domain Laws (Price Ceiling)
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = request.EventId,
            TierId = request.TierId,
            SellerId = sellerId,
            OriginalTicketCode = request.OriginalTicketCode,
            OriginalPrice = request.OriginalPrice,
            ResalePrice = request.ResalePrice,
            IsPrivate = request.IsPrivate,
            PrivateAccessToken = privateAccessToken,
            VerificationStatus = VerificationStatus.Verified,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Domain-level validation: ResalePrice <= OriginalPrice
        listing.ValidatePriceCeiling();

        _dbContext.ResaleListings.Add(listing);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var responseData = new CreateResaleListingResponse
        {
            ListingId = listing.Id,
            EventId = listing.EventId,
            TierId = listing.TierId,
            OriginalTicketCode = listing.OriginalTicketCode,
            OriginalPrice = listing.OriginalPrice,
            ResalePrice = listing.ResalePrice,
            IsPrivate = listing.IsPrivate,
            PrivateAccessToken = listing.PrivateAccessToken,
            ShareUrl = shareUrl,
            VerificationStatus = listing.VerificationStatus.ToString(),
            ListingStatus = listing.ListingStatus.ToString(),
            CreatedAt = listing.CreatedAt
        };

        var message = listing.IsPrivate
            ? "Đăng bán vé thành công ở chế độ Riêng tư (Private Sale). Hãy dùng liên kết bảo mật để chia sẻ cho người mua."
            : "Đăng bán vé thành công ở chế độ Công khai (Public Sale).";

        return ApiResponse<CreateResaleListingResponse>.SuccessResponse(responseData, message);
    }
}
