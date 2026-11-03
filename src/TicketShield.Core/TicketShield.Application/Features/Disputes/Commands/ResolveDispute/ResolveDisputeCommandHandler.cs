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
                    .SetProperty(row => row.InSettlementBuffer, false)
                    .SetProperty(row => row.UpdatedAt, now),
                cancellationToken);
        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new BusinessRuleViolationException("Khoản tiền không còn bị giữ để xử lý khiếu nại.");
        }

        var resolver = await _db.ShadowUsers.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (resolver == null)
        {
            resolver = new ShadowUser
            {
                Id = userId,
                Email = _currentUser.Email ?? "staff@ticketshield.vn",
                FullName = _currentUser.FullName ?? _currentUser.Role ?? "Moderator",
                Role = _currentUser.Role == "Admin" ? UserRole.Admin : UserRole.Moderator,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _db.ShadowUsers.AddAsync(resolver, cancellationToken);
        }

        dispute.AdminNotes = request.Reason.Trim();
        dispute.ResolvedBy = userId;
        dispute.ResolvedAt = now;
        dispute.UpdatedAt = now;
        dispute.Escrow.Status = nextEscrowStatus;
        if (decision == DisputeDecision.Approve)
        {
            dispute.Status = DisputeStatus.Resolved;
            dispute.Resolution = DisputeResolution.RefundBuyer;
            dispute.RefundAmount = dispute.Escrow.TotalBuyerPaid;

            // FIX BUG "Vé ma" (US-10.2 / SCRUM-325): Cascade Listing Invalidation
            // Buyer mua vé rồi đăng bán lại. Khi Admin duyệt Refund đơn mua gốc,
            // toàn bộ Listing mà Buyer đã tạo từ vé này phải bị Cancelled ngay lập tức,
            // giải phóng giữ chỗ nếu đang Transacting, và đánh dấu TicketStatus = Cancelled.
            var ticketTokens = (dispute.Escrow.NewTicketCode ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (ticketTokens.Count == 0 && dispute.Escrow.ListingId != Guid.Empty)
            {
                var originalCode = await _db.ResaleListings
                    .Where(l => l.Id == dispute.Escrow.ListingId)
                    .Select(l => l.OriginalTicketCode)
                    .FirstOrDefaultAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(originalCode))
                {
                    ticketTokens.Add(originalCode);
                }
            }

            var downstreamListings = await _db.ResaleListings
                .Include(l => l.Ticket)
                .Where(l => l.SellerId == dispute.Escrow.BuyerId)
                .Where(l => l.ListingStatus == ListingStatus.Verified || l.ListingStatus == ListingStatus.Transacting)
                .ToListAsync(cancellationToken);

            foreach (var downstream in downstreamListings)
            {
                var matchesCode = ticketTokens.Contains(downstream.OriginalTicketCode)
                    || (downstream.Ticket != null && ticketTokens.Contains(downstream.Ticket.TicketCode));

                if (matchesCode)
                {
                    downstream.ListingStatus = ListingStatus.Cancelled;
                    downstream.UpdatedAt = now;

                    if (downstream.Ticket != null)
                    {
                        downstream.Ticket.Status = TicketStatus.Cancelled;
                        downstream.Ticket.UpdatedAt = now;
                    }

                    // Nếu đang có ai đó giữ chỗ (Pending escrow), hủy luôn escrow giữ chỗ
                    var downstreamEscrows = await _db.EscrowTransactions
                        .Where(e => e.ListingId == downstream.Id && e.Status == EscrowStatus.Pending)
                        .ToListAsync(cancellationToken);

                    foreach (var de in downstreamEscrows)
                    {
                        de.Status = EscrowStatus.Cancelled;
                        de.UnlockAt = now;
                        de.UpdatedAt = now;
                    }
                }
            }

            // Đồng thời cập nhật trạng thái các Ticket entity tương ứng nếu tồn tại
            if (ticketTokens.Count > 0)
            {
                var affectedTickets = await _db.Tickets
                    .Where(t => ticketTokens.Contains(t.TicketCode))
                    .ToListAsync(cancellationToken);

                foreach (var t in affectedTickets)
                {
                    t.Status = TicketStatus.Cancelled;
                    t.UpdatedAt = now;
                }
            }
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
