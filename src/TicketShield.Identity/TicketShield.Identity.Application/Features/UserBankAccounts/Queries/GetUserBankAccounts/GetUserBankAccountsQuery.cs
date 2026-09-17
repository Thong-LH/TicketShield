using MediatR;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.UserBankAccounts.Dtos;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Queries.GetUserBankAccounts;

public class GetUserBankAccountsQuery : IRequest<ApiResponse<List<UserBankAccountDto>>>
{
}
