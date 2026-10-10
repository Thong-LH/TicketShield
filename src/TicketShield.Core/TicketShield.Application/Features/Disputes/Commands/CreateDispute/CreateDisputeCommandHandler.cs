using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Disputes.Commands.CreateDispute;

public class CreateDisputeCommandHandler : IRequestHandler<CreateDisputeCommand, ApiResponse<CreateDisputeResponse>>
{
    private readonly ITicketShieldDbContext _db;
    private readonly ICurrentUserService? _currentUser;

    public CreateDisputeCommandHandler(ITicketShieldDbContext db, ICurrentUserService? currentUser = null)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<CreateDisputeResponse>> Handle(CreateDisputeCommand request, CancellationToken cancellationToken)
    {
        var buyerId = _currentUser?.UserId ?? Guid.Empty;
        if (buyerId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để gửi khiếu nại.");
        }

        var escrow = await _db.EscrowTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == request.EscrowId, cancellationToken);
        if (escrow == null)
        {
            throw new NotFoundException("Giao dịch", request.EscrowId);
        }

        if (escrow.BuyerId != buyerId)
        {
            throw new ForbiddenAccessException("Bạn không có quyền gửi khiếu nại cho giao dịch này.");
        }

        var now = DateTimeOffset.UtcNow;
        var disputeId = Guid.NewGuid();
        var dispute = new Dispute
        {
            Id = disputeId,
            EscrowId = escrow.Id,
            BuyerId = buyerId,
            DisputeCode = $"DP-{disputeId.ToString("N")[..8].ToUpperInvariant()}",
            ReasonCode = request.ReasonCode,
            Description = request.Reason.Trim(),
            Status = DisputeStatus.Open,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await _db.EscrowTransactions
            .Where(row => row.Id == escrow.Id && row.Status == EscrowStatus.Locked)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.Status, EscrowStatus.Disputed)
                    .SetProperty(row => row.UpdatedAt, now),
                cancellationToken);

        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DisputeDeadlineExpiredException();
        }

        _db.Disputes.Add(dispute);
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = nameof(DisputeHarvestRequested),
            Payload = JsonSerializer.Serialize(new DisputeHarvestRequested { DisputeId = dispute.Id }),
            CreatedAt = now,
            UpdatedAt = now
        });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return ApiResponse<CreateDisputeResponse>.SuccessResponse(
            new CreateDisputeResponse
            {
                DisputeId = dispute.Id,
                DisputeCode = dispute.DisputeCode,
                CreatedAt = dispute.CreatedAt
            },
            "Khiếu nại đã được ghi nhận.");
    }
}
