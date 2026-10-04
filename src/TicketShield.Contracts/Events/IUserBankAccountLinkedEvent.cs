namespace TicketShield.Contracts.Events;

/// <summary>
/// Phát ra từ Identity khi người bán lưu tài khoản ngân hàng nhận tiền.
/// Core ghi bản sao lên ShadowUser để worker giải ngân đọc được mà không cần token.
/// </summary>
public interface IUserBankAccountLinkedEvent
{
    Guid UserId { get; }
    string BankCode { get; }
    string AccountNumber { get; }
    string AccountHolderName { get; }
}

public record UserBankAccountLinkedEvent(
    Guid UserId,
    string BankCode,
    string AccountNumber,
    string AccountHolderName
) : IUserBankAccountLinkedEvent;
