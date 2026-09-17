using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.UserBankAccounts.Dtos;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Queries.GetUserBankAccounts;

public class GetUserBankAccountsQueryHandler : IRequestHandler<GetUserBankAccountsQuery, ApiResponse<List<UserBankAccountDto>>>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public GetUserBankAccountsQueryHandler(
        IIdentityDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<List<UserBankAccountDto>>> Handle(GetUserBankAccountsQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để xem danh sách tài khoản ngân hàng.");
        }

        var bankAccounts = await _dbContext.UserBankAccounts
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.IsDefault)
            .ThenByDescending(b => b.CreatedAt)
            .Select(b => new UserBankAccountDto
            {
                Id = b.Id,
                UserId = b.UserId,
                BankCode = b.BankCode,
                BankAccountNumber = b.BankAccountNumber,
                AccountHolderName = b.AccountHolderName,
                IsDefault = b.IsDefault,
                CreatedAt = b.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<List<UserBankAccountDto>>.SuccessResponse(bankAccounts, "Lấy danh sách tài khoản ngân hàng thành công.");
    }
}
