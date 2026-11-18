using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Contracts.Events;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.UserBankAccounts.Dtos;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Commands.SetDefaultUserBankAccount;

public class SetDefaultUserBankAccountCommandHandler : IRequestHandler<SetDefaultUserBankAccountCommand, ApiResponse<UserBankAccountDto>>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPublishEndpoint _publishEndpoint;

    public SetDefaultUserBankAccountCommandHandler(
        IIdentityDbContext dbContext,
        ICurrentUserService currentUserService,
        IPublishEndpoint publishEndpoint)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<ApiResponse<UserBankAccountDto>> Handle(SetDefaultUserBankAccountCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để thực hiện thao tác này.");
        }

        var userAccounts = await _dbContext.UserBankAccounts
            .Where(b => b.UserId == userId)
            .ToListAsync(cancellationToken);

        var targetAccount = userAccounts.FirstOrDefault(b => b.Id == request.BankAccountId);
        if (targetAccount is null)
        {
            throw new NotFoundException("Tài khoản ngân hàng", request.BankAccountId);
        }

        // Idempotent: Nếu tài khoản đã là Default rồi thì không cần cập nhật lại
        if (!targetAccount.IsDefault)
        {
            foreach (var account in userAccounts)
            {
                account.IsDefault = false;
            }

            targetAccount.IsDefault = true;

            // Publish event để Core cập nhật snapshot Payout cho Seller
            await _publishEndpoint.Publish<IUserBankAccountLinkedEvent>(new UserBankAccountLinkedEvent(
                UserId: userId,
                BankCode: targetAccount.BankCode,
                AccountNumber: targetAccount.BankAccountNumber,
                AccountHolderName: targetAccount.AccountHolderName
            ), cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var dto = new UserBankAccountDto
        {
            Id = targetAccount.Id,
            UserId = targetAccount.UserId,
            BankCode = targetAccount.BankCode,
            BankAccountNumber = targetAccount.BankAccountNumber,
            AccountHolderName = targetAccount.AccountHolderName,
            IsDefault = targetAccount.IsDefault,
            CreatedAt = targetAccount.CreatedAt
        };

        return ApiResponse<UserBankAccountDto>.SuccessResponse(dto, "Đặt tài khoản ngân hàng mặc định thành công.");
    }
}
