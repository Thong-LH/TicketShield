using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Contracts.Events;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.UserBankAccounts.Dtos;
using TicketShield.Identity.Domain.Entities;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Commands.CreateUserBankAccount;

public class CreateUserBankAccountCommandHandler : IRequestHandler<CreateUserBankAccountCommand, ApiResponse<UserBankAccountDto>>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPublishEndpoint _publishEndpoint;

    public CreateUserBankAccountCommandHandler(
        IIdentityDbContext dbContext,
        ICurrentUserService currentUserService,
        IPublishEndpoint publishEndpoint)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<ApiResponse<UserBankAccountDto>> Handle(CreateUserBankAccountCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedException("You must be signed in to add a bank account.");
        }

        if (string.IsNullOrWhiteSpace(request.BankCode))
        {
            throw new BadRequestException("Bank code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.BankAccountNumber))
        {
            throw new BadRequestException("Bank account number is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AccountHolderName))
        {
            throw new BadRequestException("Account holder name is required.");
        }

        var existingAccounts = await _dbContext.UserBankAccounts
            .Where(b => b.UserId == userId)
            .ToListAsync(cancellationToken);

        // Guard 1: Limit maximum 5 bank accounts / user
        if (existingAccounts.Count >= 5)
        {
            throw new BadRequestException("You can link up to 5 bank accounts.");
        }

        // Guard 2: Prevent duplicate BankCode + AccountNumber for same user
        var normalizedAccountNumber = request.BankAccountNumber.Trim();
        var normalizedBankCode = request.BankCode.Trim();

        var isDuplicate = existingAccounts.Any(b =>
            string.Equals(b.BankCode, normalizedBankCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(b.BankAccountNumber, normalizedAccountNumber, StringComparison.OrdinalIgnoreCase));

        if (isDuplicate)
        {
            throw new BadRequestException("This bank account has already been linked.");
        }

        // If this is the user's first bank account, force it to be default
        var isDefault = !existingAccounts.Any() || request.IsDefault;

        if (isDefault && existingAccounts.Any())
        {
            foreach (var acc in existingAccounts)
            {
                acc.IsDefault = false;
            }
        }

        var bankAccount = new UserBankAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BankCode = request.BankCode.Trim(),
            BankAccountNumber = request.BankAccountNumber.Trim(),
            AccountHolderName = request.AccountHolderName.Trim().ToUpper(),
            IsDefault = isDefault,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.UserBankAccounts.Add(bankAccount);

        var published = isDefault
            ? bankAccount
            : existingAccounts.FirstOrDefault(account => account.IsDefault) ?? bankAccount;

        await _publishEndpoint.Publish<IUserBankAccountLinkedEvent>(new UserBankAccountLinkedEvent(
            UserId: userId,
            BankCode: published.BankCode,
            AccountNumber: published.BankAccountNumber,
            AccountHolderName: published.AccountHolderName
        ), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new UserBankAccountDto
        {
            Id = bankAccount.Id,
            UserId = bankAccount.UserId,
            BankCode = bankAccount.BankCode,
            BankAccountNumber = bankAccount.BankAccountNumber,
            AccountHolderName = bankAccount.AccountHolderName,
            IsDefault = bankAccount.IsDefault,
            CreatedAt = bankAccount.CreatedAt
        };

        return ApiResponse<UserBankAccountDto>.SuccessResponse(dto, "Bank account linked successfully.");
    }
}
