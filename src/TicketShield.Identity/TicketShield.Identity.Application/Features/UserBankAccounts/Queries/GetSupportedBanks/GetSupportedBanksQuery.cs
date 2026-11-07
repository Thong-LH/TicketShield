using MediatR;
using TicketShield.Identity.Application.Common.Models;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Queries.GetSupportedBanks;

public sealed record GetSupportedBanksQuery : IRequest<ApiResponse<List<BankDirectoryItem>>>;

public sealed record BankDirectoryItem(string Code, string Name, string Bin, string ShortName);
