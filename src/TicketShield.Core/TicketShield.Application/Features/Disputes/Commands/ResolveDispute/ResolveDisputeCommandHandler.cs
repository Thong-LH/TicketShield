using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Helpers;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Disputes.Commands.ResolveDispute;

public class ResolveDisputeCommandHandler : IRequestHandler<ResolveDisputeCommand, ApiResponse<ResolveDisputeResponse>>
{
    private readonly ITicketShieldDbContext _db;
    private readonly ICurrentUserService? _currentUser;

    public ResolveDisputeCommandHandler(ITicketShieldDbContext db, ICurrentUserService? currentUser = null)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<ResolveDisputeResponse>> Handle(ResolveDisputeCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser is not { IsAuthenticated: true, UserId: { } userId } || userId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để xử lý khiếu nại.");
        }

        if (_currentUser.Role is not ("Admin" or "Cskh" or "Moderator"))
        {
            throw new ForbiddenAccessException("Bạn không có quyền xử lý khiếu nại này.");
        }

        var dispute = await _db.Disputes
            .Include(row => row.Escrow)
            .ThenInclude(row => row.Seller)
            .Include(row => row.Escrow)
            .ThenInclude(row => row.Buyer)
            .FirstOrDefaultAsync(row => row.Id == request.DisputeId, cancellationToken);
        if (dispute == null)
        {
            throw new NotFoundException("Khiếu nại", request.DisputeId);
        }

        if (dispute.Status != DisputeStatus.Open)
        {
            throw new BusinessRuleViolationException("Khiếu nại này đã được xử lý.");
        }

        var decision = request.Decision ?? throw new BusinessRuleViolationException("Quyết định không hợp lệ.");
        if (decision == DisputeDecision.Approve)
        {
            if (dispute.Escrow.Buyer == null)
            {
                var buyer = await _db.ShadowUsers.FirstOrDefaultAsync(u => u.Id == dispute.Escrow.BuyerId, cancellationToken);
                if (buyer == null)
                {
                    buyer = new ShadowUser { Id = dispute.Escrow.BuyerId };
                    _db.ShadowUsers.Add(buyer);
                }
                dispute.Escrow.Buyer = buyer;
            }

            if (!PayoutAccountRules.IsPresent(dispute.Escrow.Buyer.PayoutAccountNumber))
            {
                await IdentityBankSyncHelper.TrySyncUserBankFromIdentityAsync(_db, dispute.Escrow.Buyer, cancellationToken);
            }

            if (!PayoutAccountRules.IsPresent(dispute.Escrow.Buyer.PayoutAccountNumber))
            {
                throw new BusinessRuleViolationException("Người mua chưa liên kết tài khoản ngân hàng để nhận tiền hoàn.");
            }
        }
        else if (decision == DisputeDecision.Reject)
        {
            if (dispute.Escrow.Seller == null)
            {
                var seller = await _db.ShadowUsers.FirstOrDefaultAsync(u => u.Id == dispute.Escrow.SellerId, cancellationToken);
                if (seller == null)
                {
                    seller = new ShadowUser { Id = dispute.Escrow.SellerId };
                    _db.ShadowUsers.Add(seller);
                }
                dispute.Escrow.Seller = seller;
            }

            if (!PayoutAccountRules.IsPresent(dispute.Escrow.Seller.PayoutAccountNumber))
            {
                await IdentityBankSyncHelper.TrySyncUserBankFromIdentityAsync(_db, dispute.Escrow.Seller, cancellationToken);
            }

            if (!PayoutAccountRules.IsPresent(dispute.Escrow.Seller.PayoutAccountNumber))
            {
                throw new BusinessRuleViolationException("Người bán chưa có tài khoản nhận tiền.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var nextEscrowStatus = decision == DisputeDecision.Approve
            ? EscrowStatus.Refunded
            : EscrowStatus.Releasing;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await _db.EscrowTransactions
            .Where(row => row.Id == dispute.EscrowId && row.Status == EscrowStatus.Disputed)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.Status, nextEscrowStatus)
                    .SetProperty(row => row.UpdatedAt, now),
                cancellationToken);
        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new BusinessRuleViolationException("Khoản tiền không còn bị giữ để xử lý khiếu nại.");
        }

        dispute.AdminNotes = request.Reason.Trim();
        dispute.ResolvedBy = userId;
        dispute.ResolvedAt = now;
        dispute.UpdatedAt = now;
        if (decision == DisputeDecision.Approve)
        {
            dispute.Status = DisputeStatus.Resolved;
            dispute.Resolution = DisputeResolution.RefundBuyer;
            dispute.RefundAmount = dispute.Escrow.TotalBuyerPaid;
        }
        else
        {
            dispute.Status = DisputeStatus.Rejected;
            dispute.Resolution = DisputeResolution.ReleaseSeller;
            dispute.RefundAmount = 0;
            await AddPayoutAsync(dispute.Escrow, now, cancellationToken);
        }

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

        return ApiResponse<ResolveDisputeResponse>.SuccessResponse(new ResolveDisputeResponse
        {
            DisputeStatus = dispute.Status.ToString(),
            EscrowStatus = nextEscrowStatus.ToString()
        });
    }

    private async Task AddPayoutAsync(EscrowTransaction escrow, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var letter = PayoutReleaseLetter.Create(escrow, escrow.Seller);
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = nameof(PayoutRequestedEvent),
            Payload = JsonSerializer.Serialize(letter),
            CreatedAt = now,
            UpdatedAt = now
        });

        var payout = await _db.PayoutTransactions
            .FirstOrDefaultAsync(row => row.EscrowId == escrow.Id, cancellationToken);
        if (payout == null)
        {
            payout = new PayoutTransaction
            {
                Id = Guid.NewGuid(),
                EscrowId = escrow.Id,
                SellerId = escrow.SellerId,
                PayoutCode = PayoutReleaseLetter.Code(escrow.Id),
                CreatedAt = now
            };
            _db.PayoutTransactions.Add(payout);
        }

        PayoutReleaseLetter.Apply(payout, letter, now);
    }
}
