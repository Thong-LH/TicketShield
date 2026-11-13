using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Helpers;
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
            .Include(row => row.Listing)
            .FirstOrDefaultAsync(row => row.Id == request.EscrowId, cancellationToken);
        if (escrow == null)
        {
            throw new NotFoundException("Giao dịch", request.EscrowId);
        }

        if (escrow.BuyerId != buyerId)
        {
            throw new ForbiddenAccessException("Bạn không có quyền gửi khiếu nại cho giao dịch này.");
        }

        if (escrow.DisputeWaived)
        {
            throw new BusinessRuleViolationException("Vé này đã được đăng bán lại, quyền khiếu nại đã chấm dứt.");
        }

        // BE-11.3.1 (SCRUM-343 / SCRUM-344): Enforce Dispute Prevention on Used Gate Tickets
        // Kiểm tra nếu vé liên kết với giao dịch đã qua cổng soát vé (TicketStatus.Used) thì từ chối khiếu nại
        var ticketTokens = (escrow.NewTicketCode ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (ticketTokens.Count == 0 && escrow.Listing != null && !string.IsNullOrWhiteSpace(escrow.Listing.OriginalTicketCode))
        {
            ticketTokens.Add(escrow.Listing.OriginalTicketCode.Trim());
        }

        var isTicketUsed = false;
        string? usedCode = null;

        if (escrow.Listing?.TicketId.HasValue == true)
        {
            var listingTicket = await _db.Tickets
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == escrow.Listing.TicketId.Value && t.Status == TicketStatus.Used, cancellationToken);
            if (listingTicket != null)
            {
                isTicketUsed = true;
                usedCode = listingTicket.TicketCode;
            }
        }

        if (!isTicketUsed && ticketTokens.Count > 0)
        {
            var matchedTicket = await _db.Tickets
                .AsNoTracking()
                .FirstOrDefaultAsync(t => ticketTokens.Contains(t.TicketCode) && t.Status == TicketStatus.Used, cancellationToken);
            if (matchedTicket != null)
            {
                isTicketUsed = true;
                usedCode = matchedTicket.TicketCode;
            }
        }

        if (isTicketUsed)
        {
            var codeDetail = !string.IsNullOrWhiteSpace(usedCode) ? $" [{usedCode}]" : "";
            throw new BusinessRuleViolationException($"Vé{codeDetail} đã qua cửa soát vé tại sự kiện (đã sử dụng), không thể gửi khiếu nại.");
        }

        var buyer = await _db.ShadowUsers.FirstOrDefaultAsync(u => u.Id == buyerId, cancellationToken);
        if (buyer == null)
        {
            buyer = new ShadowUser { Id = buyerId };
            _db.ShadowUsers.Add(buyer);
        }

        if (!PayoutAccountRules.IsPresent(buyer.PayoutAccountNumber))
        {
            await IdentityBankSyncHelper.TrySyncUserBankFromIdentityAsync(_db, buyer, cancellationToken);
        }

        if (!PayoutAccountRules.IsPresent(buyer.PayoutAccountNumber))
        {
            throw new BusinessRuleViolationException("Bạn cần liên kết tài khoản ngân hàng trước khi gửi báo cáo sự cố để có thể nhận tiền hoàn.");
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
            .Where(row => row.Id == escrow.Id && row.Status == EscrowStatus.Locked && !row.DisputeWaived)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.Status, EscrowStatus.Disputed)
                    .SetProperty(row => row.InSettlementBuffer, false)
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
