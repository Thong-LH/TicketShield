using MediatR;
using TicketShield.Identity.Application.Common.Models;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Queries.LookupAccountHolderName;

/// <summary>
/// BE-CORE-3.1.5: Lookup bank account holder name via VietQR API (proxy to avoid browser CORS).
/// </summary>
public class LookupAccountHolderNameQuery : IRequest<ApiResponse<LookupAccountHolderNameResult>>
{
    public string BankBin { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;

    public LookupAccountHolderNameQuery(string bankBin, string accountNumber)
    {
        BankBin = bankBin;
        AccountNumber = accountNumber;
    }
}

public class LookupAccountHolderNameResult
{
    public bool Success { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
}
