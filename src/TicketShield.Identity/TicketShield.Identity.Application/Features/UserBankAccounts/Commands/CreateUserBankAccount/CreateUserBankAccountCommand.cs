using MediatR;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.UserBankAccounts.Dtos;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Commands.CreateUserBankAccount;

public class CreateUserBankAccountCommand : IRequest<ApiResponse<UserBankAccountDto>>
{
    public string BankCode { get; set; } = string.Empty;
    public string BankAccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public bool IsDefault { get; set; } = true;

    public CreateUserBankAccountCommand() { }

    public CreateUserBankAccountCommand(string bankCode, string bankAccountNumber, string accountHolderName, bool isDefault = true)
    {
        BankCode = bankCode;
        BankAccountNumber = bankAccountNumber;
        AccountHolderName = accountHolderName;
        IsDefault = isDefault;
    }
}
