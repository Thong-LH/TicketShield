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
            throw new UnauthorizedException("You must be signed in to perform this action.");
        }

        var userAccounts = await _dbContext.UserBankAccounts
            .Where(b => b.UserId == userId)
            .ToListAsync(cancellationToken);

        var targetAccount = userAccounts.FirstOrDefault(b => b.Id == request.BankAccountId);
        if (targetAccount is null)
        {
            throw new NotFoundException("Bank account", request.BankAccountId);
        }

        // Idempotent: If account is already Default, no update needed
        if (!targetAccount.IsDefault)
        {
            foreach (var account in userAccounts)
            {
                account.IsDefault = false;
            }

            targetAccount.IsDefault = true;

            // Publish event so Core can update Seller Payout snapshot
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

        return ApiResponse<UserBankAccountDto>.SuccessResponse(dto, "Default bank account set successfully.");
    }
}
