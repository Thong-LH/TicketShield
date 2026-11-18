using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Contracts.Events;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Commands.DeleteUserBankAccount;

public class DeleteUserBankAccountCommandHandler : IRequestHandler<DeleteUserBankAccountCommand, ApiResponse<object>>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPublishEndpoint _publishEndpoint;

    public DeleteUserBankAccountCommandHandler(
        IIdentityDbContext dbContext,
        ICurrentUserService currentUserService,
        IPublishEndpoint publishEndpoint)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<ApiResponse<object>> Handle(DeleteUserBankAccountCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để thực hiện thao tác này.");
        }

        // Step 1: Tìm TK cần xóa — phải thuộc đúng user đang đăng nhập
        var targetAccount = await _dbContext.UserBankAccounts
            .FirstOrDefaultAsync(b => b.Id == request.BankAccountId && b.UserId == userId, cancellationToken);

        if (targetAccount is null)
        {
            throw new NotFoundException("Tài khoản ngân hàng", request.BankAccountId);
        }

        // Step 2: Lấy tất cả TK còn lại (ngoại trừ TK cần xóa)
        var remainingAccounts = await _dbContext.UserBankAccounts
            .Where(b => b.UserId == userId && b.Id != request.BankAccountId)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync(cancellationToken);

        // Step 3: Không cho xóa TK Default duy nhất còn lại
        if (targetAccount.IsDefault && !remainingAccounts.Any())
        {
            throw new BadRequestException(
                "Không thể xóa tài khoản mặc định duy nhất. Vui lòng thêm tài khoản khác hoặc đặt tài khoản khác làm mặc định trước.");
        }

        // Step 4: Xóa TK khỏi DB
        _dbContext.UserBankAccounts.Remove(targetAccount);

        // Step 5: Nếu TK bị xóa là Default → tự động nâng TK cũ nhất còn lại thành Default
        //         và publish event để Core cập nhật snapshot payout
        if (targetAccount.IsDefault)
        {
            var newDefault = remainingAccounts.First(); // đã OrderBy CreatedAt ASC
            newDefault.IsDefault = true;

            await _publishEndpoint.Publish<IUserBankAccountLinkedEvent>(new UserBankAccountLinkedEvent(
                UserId: userId,
                BankCode: newDefault.BankCode,
                AccountNumber: newDefault.BankAccountNumber,
                AccountHolderName: newDefault.AccountHolderName
            ), cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<object>.SuccessResponse(new { }, "Xóa tài khoản ngân hàng thành công.");
    }
}
