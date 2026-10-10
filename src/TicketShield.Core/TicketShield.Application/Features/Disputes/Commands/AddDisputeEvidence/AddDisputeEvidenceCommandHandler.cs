using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Disputes.Commands.AddDisputeEvidence;

public class AddDisputeEvidenceCommandHandler : IRequestHandler<AddDisputeEvidenceCommand, ApiResponse<AddDisputeEvidenceResponse>>
{
    private readonly ITicketShieldDbContext _db;
    private readonly IDisputeEvidenceFileStore _files;
    private readonly ICurrentUserService? _currentUser;

    public AddDisputeEvidenceCommandHandler(
        ITicketShieldDbContext db,
        IDisputeEvidenceFileStore files,
        ICurrentUserService? currentUser = null)
    {
        _db = db;
        _files = files;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<AddDisputeEvidenceResponse>> Handle(AddDisputeEvidenceCommand request, CancellationToken cancellationToken)
    {
        var buyerId = _currentUser?.UserId ?? Guid.Empty;
        if (buyerId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để gửi ảnh khiếu nại.");
        }

        if (!DisputeEvidenceImages.TryGetFormat(request.Content, request.ContentType, out var extension, out _))
        {
            throw new BadRequestException("Ảnh không hợp lệ. Chỉ nhận ảnh JPEG, PNG hoặc WebP tối đa 5 MB.");
        }

        var dispute = await _db.Disputes
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == request.DisputeId, cancellationToken);
        if (dispute == null)
        {
            throw new NotFoundException("Khiếu nại", request.DisputeId);
        }

        if (dispute.BuyerId != buyerId)
        {
            throw new ForbiddenAccessException("Bạn không có quyền gửi ảnh cho khiếu nại này.");
        }

        var key = await _files.SaveAsync(request.Content, extension, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var evidence = new DisputeEvidence
        {
            Id = Guid.NewGuid(),
            DisputeId = dispute.Id,
            UploaderId = buyerId,
            EvidenceType = "IMAGE",
            FileUrl = key,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            _db.DisputeEvidences.Add(evidence);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _files.DeleteAsync(key, cancellationToken);
            throw;
        }

        return ApiResponse<AddDisputeEvidenceResponse>.SuccessResponse(
            new AddDisputeEvidenceResponse { EvidenceId = evidence.Id },
            "Ảnh khiếu nại đã được ghi nhận.");
    }
}
