using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Disputes.Queries.GetDisputeEvidenceFile;

public class GetDisputeEvidenceFileQueryHandler : IRequestHandler<GetDisputeEvidenceFileQuery, DisputeEvidenceFileResult>
{
    private readonly ITicketShieldDbContext _db;
    private readonly IDisputeEvidenceFileStore _files;
    private readonly ICurrentUserService? _currentUser;

    public GetDisputeEvidenceFileQueryHandler(
        ITicketShieldDbContext db,
        IDisputeEvidenceFileStore files,
        ICurrentUserService? currentUser = null)
    {
        _db = db;
        _files = files;
        _currentUser = currentUser;
    }

    public async Task<DisputeEvidenceFileResult> Handle(GetDisputeEvidenceFileQuery request, CancellationToken cancellationToken)
    {
        var buyerId = _currentUser?.UserId ?? Guid.Empty;
        if (buyerId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để xem ảnh khiếu nại.");
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
            throw new ForbiddenAccessException("Bạn không có quyền xem ảnh khiếu nại này.");
        }

        var evidence = await _db.DisputeEvidences
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == request.EvidenceId && row.DisputeId == dispute.Id, cancellationToken);
        if (evidence == null)
        {
            throw new NotFoundException("Ảnh khiếu nại", request.EvidenceId);
        }

        var content = await _files.ReadAsync(evidence.FileUrl, cancellationToken);
        if (content == null)
        {
            throw new NotFoundException("Ảnh khiếu nại", request.EvidenceId);
        }

        return new DisputeEvidenceFileResult
        {
            Content = content,
            ContentType = DisputeEvidenceImages.MediaTypeFor(evidence.FileUrl)
        };
    }
}
