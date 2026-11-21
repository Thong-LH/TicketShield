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
            throw new UnauthorizedException("You must be signed in to perform this action.");
        }

        // Step 1: Find account to delete — must belong to current user
        var targetAccount = await _dbContext.UserBankAccounts
            .FirstOrDefaultAsync(b => b.Id == request.BankAccountId && b.UserId == userId, cancellationToken);

        if (targetAccount is null)
        {
            throw new NotFoundException("Bank account", request.BankAccountId);
        }

        // Step 2: Get all remaining accounts (excluding the one being deleted)
        var remainingAccounts = await _dbContext.UserBankAccounts
            .Where(b => b.UserId == userId && b.Id != request.BankAccountId)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync(cancellationToken);

        // Step 3: Prevent deleting the only default account remaining
        if (targetAccount.IsDefault && !remainingAccounts.Any())
        {
            throw new BadRequestException(
                "You cannot remove your only default payout account. Add another account or make a different account the default first.");
        }

        // Step 4: Remove account from DB
        _dbContext.UserBankAccounts.Remove(targetAccount);

        // Step 5: If deleted account was Default -> promote oldest remaining account to Default
        if (targetAccount.IsDefault)
        {
            var newDefault = remainingAccounts.First(); // Ordered by CreatedAt ASC
            newDefault.IsDefault = true;

            await _publishEndpoint.Publish<IUserBankAccountLinkedEvent>(new UserBankAccountLinkedEvent(
                UserId: userId,
                BankCode: newDefault.BankCode,
                AccountNumber: newDefault.BankAccountNumber,
                AccountHolderName: newDefault.AccountHolderName
            ), cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<object>.SuccessResponse(new { }, "Bank account deleted successfully.");
    }
}
