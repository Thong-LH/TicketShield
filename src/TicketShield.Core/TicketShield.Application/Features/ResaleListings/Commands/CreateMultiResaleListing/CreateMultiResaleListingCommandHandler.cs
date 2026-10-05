using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Resale;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.CreateMultiResaleListing;

/// <summary>
/// Handler điều phối đăng bán danh sách vé thành bundle qua MediatR (SCRUM-167 / BE-CORE-5.2.2).
/// Thực thi nguyên khối trong 1 Database Transaction duy nhất (ACID) với 1 Root Idempotency-Key.
/// </summary>
public class CreateMultiResaleListingCommandHandler : IRequestHandler<CreateMultiResaleListingCommand, ApiResponse<BulkPublishResult>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ITicketVerificationService? _verificationService;
    private readonly ICurrentUserService? _currentUserService;
    private readonly ILogger<CreateMultiResaleListingCommandHandler>? _logger;

    public CreateMultiResaleListingCommandHandler(
        ITicketShieldDbContext dbContext,
        ITicketVerificationService? verificationService = null,
        ICurrentUserService? currentUserService = null,
        ILogger<CreateMultiResaleListingCommandHandler>? logger = null)
    {
        _dbContext = dbContext;
        _verificationService = verificationService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<BulkPublishResult>> Handle(CreateMultiResaleListingCommand request, CancellationToken cancellationToken)
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

        if (request.Body.Items.Count < 2 || request.Body.Items.Count > ResaleListing.MaxBundleTickets)
        {
            throw new ResaleWorkflowException("BUNDLE_REQUIRES_2_TO_3_ITEMS", 400);
        }

        if (request.Body.Items.Any(i => string.IsNullOrWhiteSpace(i.VerificationId)))
        {
            throw new ResaleWorkflowException("BULK_ITEM_MISSING_VERIFICATION_ID", 400);
        }

        if (request.Body.Items.Select(i => i.VerificationId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Body.Items.Count)
        {
            throw new ResaleWorkflowException("DUPLICATE_VERIFICATION_IN_BATCH", 400);
        }

        if (_verificationService == null)
        {
            throw new ResaleWorkflowException("SERVICE_UNAVAILABLE", 503);
        }

        // 3. Xử lý 1 Root Idempotency-Key duy nhất
        var rootKey = !string.IsNullOrWhiteSpace(request.RootIdempotencyKey)
            ? request.RootIdempotencyKey
            : Guid.NewGuid().ToString("D");

        var rootGuid = Guid.TryParse(rootKey, out var parsedGuid) ? parsedGuid : GenerateGuidFromString(rootKey);

        // 4. BẮT BUỘC dùng 1 Database Transaction duy nhất (ACID)
        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var bundleId = Guid.NewGuid();
            var publishedItems = new List<BulkPublishItemResult>();

            for (int i = 0; i < request.Body.Items.Count; i++)
            {
                var item = request.Body.Items[i];
                var itemKey = ComputeItemKey(rootGuid, i, item.VerificationId);

                // Chèn sẵn bundle_id ngay từ lệnh INSERT đầu tiên
                var publishBody = new PublishBody(
                    item.VerificationId,
                    item.ResalePrice,
                    item.IsPrivate);

                var publishResult = await _verificationService.PublishBundleItem(
                    seller,
                    itemKey.ToString("D"),
                    publishBody,
                    bundleId,
                    request.Body.Items.Count,
                    request.Body.AllOrNothing,
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

            // 5. Kiểm tra toàn bộ vé trong gói phải thuộc cùng một EventId
            var listingIds = publishedItems.Select(p => p.ListingId).ToList();
            var eventIds = await _dbContext.ResaleListings
                .Where(l => listingIds.Contains(l.Id))
                .Select(l => l.EventId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (eventIds.Count > 1)
            {
                throw new ResaleWorkflowException("BUNDLE_ITEMS_MUST_BELONG_TO_SAME_EVENT", 400);
            }

            // Commit toàn bộ gói vé trong 1 transaction ACID duy nhất
            await tx.CommitAsync(cancellationToken);

            var result = new BulkPublishResult(
                bundleId,
                request.Body.AllOrNothing,
                request.Body.Items.Count,
                publishedItems);

            return ApiResponse<BulkPublishResult>.SuccessResponse(result, "Đăng bán danh sách vé thành công.");
        }
        catch (Exception ex)
        {
            // Nếu bất kỳ vé nào gặp sự cố, rollback toàn bộ transaction ngay lập tức, không để vé mồ côi
            await tx.RollbackAsync(cancellationToken);
            _logger?.LogError(ex, "Đăng bán batch vé thất bại, đã rollback toàn bộ database transaction.");
            throw;
        }
    }

    private static Guid ComputeItemKey(Guid rootKey, int index, string verificationId)
    {
        var input = Encoding.UTF8.GetBytes($"{rootKey:D}:{index}:{verificationId}");
        var hash = MD5.HashData(input);
        return new Guid(hash);
    }

    private static Guid GenerateGuidFromString(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = MD5.HashData(bytes);
        return new Guid(hash);
    }
}
