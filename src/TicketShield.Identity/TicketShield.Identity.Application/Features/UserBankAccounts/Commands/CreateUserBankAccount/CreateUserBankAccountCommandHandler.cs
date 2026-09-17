using MediatR;
using Microsoft.EntityFrameworkCore;
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

    public CreateUserBankAccountCommandHandler(
        IIdentityDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<UserBankAccountDto>> Handle(CreateUserBankAccountCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để thêm tài khoản ngân hàng.");
        }

        if (string.IsNullOrWhiteSpace(request.BankCode))
        {
            throw new BadRequestException("Mã ngân hàng (BankCode) không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(request.BankAccountNumber))
        {
            throw new BadRequestException("Số tài khoản ngân hàng không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(request.AccountHolderName))
        {
            throw new BadRequestException("Tên chủ tài khoản ngân hàng không được để trống.");
        }

        var existingAccounts = await _dbContext.UserBankAccounts
            .Where(b => b.UserId == userId)
            .ToListAsync(cancellationToken);

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

        return ApiResponse<UserBankAccountDto>.SuccessResponse(dto, "Liên kết tài khoản ngân hàng thành công.");
    }
}
