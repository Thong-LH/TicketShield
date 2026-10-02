using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Resale;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.BulkPublishListings;

/// <summary>
/// Handler điều phối đăng bán danh sách vé thành bundle qua MediatR (SCRUM-167 / BE-CORE-5.2.2)
/// </summary>
public class BulkPublishListingsCommandHandler : IRequestHandler<BulkPublishListingsCommand, ApiResponse<BulkPublishResult>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ITicketVerificationService? _verificationService;
    private readonly ICurrentUserService? _currentUserService;
    private readonly ILogger<BulkPublishListingsCommandHandler>? _logger;

    public BulkPublishListingsCommandHandler(
        ITicketShieldDbContext dbContext,
        ITicketVerificationService? verificationService = null,
        ICurrentUserService? currentUserService = null,
        ILogger<BulkPublishListingsCommandHandler>? logger = null)
    {
        _dbContext = dbContext;
        _verificationService = verificationService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<BulkPublishResult>> Handle(BulkPublishListingsCommand request, CancellationToken cancellationToken)
    {
        // 1. Xác định seller & kiểm tra đăng nhập
        var seller = !string.IsNullOrWhiteSpace(request.Seller)
            ? request.Seller
            : _currentUserService?.UserId?.ToString("D");

        if (string.IsNullOrWhiteSpace(seller))
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để thực hiện đăng bán vé.");
        }

        // 2. Validate dữ liệu đầu vào
        if (request.Body?.Items == null || request.Body.Items.Count == 0)
        {
            throw new ResaleWorkflowException("BULK_ITEMS_EMPTY", 400);
        }

        if (request.Body.Items.Count < 2)
        {
            throw new ResaleWorkflowException("BULK_REQUIRES_MIN_2_ITEMS", 400);
        }

        if (request.Body.Items.Any(i => i.IdempotencyKey == Guid.Empty))
        {
            throw new ResaleWorkflowException("INVALID_IDEMPOTENCY_KEY", 400);
        }

        if (request.Body.Items.Select(i => i.VerificationId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Body.Items.Count)
        {
            throw new ResaleWorkflowException("DUPLICATE_VERIFICATION_IN_BATCH", 400);
        }

        if (_verificationService == null)
        {
            throw new ResaleWorkflowException("SERVICE_UNAVAILABLE", 503);
        }

        // 3. Niêm yết tuần tự từng vé với cơ chế bù trừ (compensation rollback) nếu có lỗi giữa chừng
        var publishedItems = new List<BulkPublishItemResult>();

        for (int i = 0; i < request.Body.Items.Count; i++)
        {
            var item = request.Body.Items[i];
            var publishBody = new PublishBody(item.VerificationId, item.ResalePrice, item.IsPrivate);

            try
            {
                var publishResult = await _verificationService.Publish(
                    seller,
                    item.IdempotencyKey.ToString("D"),
                    publishBody,
                    cancellationToken);

                if (publishResult.ListingId == null)
                {
                    throw new ResaleWorkflowException("PUBLISH_FAILED_NO_LISTING_ID", 500);
                }

                publishedItems.Add(new BulkPublishItemResult(
                    publishResult.ListingId.Value,
                    item.VerificationId,
                    publishResult.Status));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Đăng bán vé {VerificationId} trong batch thất bại. Tiến hành hủy {Count} vé đã niêm yết trước đó.",
                    item.VerificationId, publishedItems.Count);

                foreach (var published in publishedItems)
                {
                    try
                    {
                        await _verificationService.CancelByListingId(
                            seller,
                            published.ListingId,
                            Guid.NewGuid().ToString("D"),
                            CancellationToken.None);
                    }
                    catch (Exception cancelEx)
                    {
                        _logger?.LogWarning(cancelEx, "Lỗi bù trừ hủy vé {ListingId} trong quá trình rollback.", published.ListingId);
                    }
                }

                throw;
            }
        }

        // 4. Cập nhật BundleId và thông tin bundle chung cho tất cả vé trong batch
        var bundleId = Guid.NewGuid();
        var listingIds = publishedItems.Select(x => x.ListingId).ToList();

        var listings = await _dbContext.ResaleListings
            .Where(l => listingIds.Contains(l.Id))
            .ToListAsync(cancellationToken);

        foreach (var listing in listings)
        {
            listing.BundleId = bundleId;
            listing.IsBundleAllOrNothing = request.Body.AllOrNothing;
            listing.BundleTotalTickets = request.Body.Items.Count;
            listing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // 5. Trả kết quả thành công
        var result = new BulkPublishResult(
            bundleId,
            request.Body.AllOrNothing,
            request.Body.Items.Count,
            publishedItems);

        return ApiResponse<BulkPublishResult>.SuccessResponse(result, "Đăng bán danh sách vé thành công.");
    }
}
