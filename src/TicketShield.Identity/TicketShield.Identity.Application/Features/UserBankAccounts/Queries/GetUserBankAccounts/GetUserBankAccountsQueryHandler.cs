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
            throw new UnauthorizedException("You must be signed in to view your bank accounts.");
        }

        var bankAccounts = await _dbContext.UserBankAccounts
            .Where(b => b.UserId == userId)
            // Preserve a stable, chronological layout in the UI. The selected payout
            // account is communicated by IsDefault rather than being moved to the top.
            .OrderByDescending(b => b.CreatedAt)
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

        return ApiResponse<List<UserBankAccountDto>>.SuccessResponse(bankAccounts, "Bank accounts retrieved successfully.");
    }
}
