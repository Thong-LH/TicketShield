namespace TicketShield.Application.Common.Models;

public static class PayoutAccountRules
{
    public static bool IsPresent(string? accountNumber) => !string.IsNullOrWhiteSpace(accountNumber);

    public static bool IsValidAccountNumber(string accountNumber)
        => accountNumber.Length is >= 6 and <= 19 && accountNumber.All(char.IsDigit);
}
