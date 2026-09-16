namespace TicketShield.Contracts.Events;

/// <summary>
/// Event bắn ra khi quyền sở hữu vé đã được chuyển giao thành công cho Buyer (gRPC BTC hoàn tất).
/// Settlement Worker sẽ tiêu thụ Event này để lên lịch đếm ngược T+24h giải ngân tiền cho Seller.
/// </summary>
public interface IOwnershipTransferredEvent
{
    Guid EscrowId { get; }
    Guid ListingId { get; }
    Guid SellerId { get; }
    decimal NetSellerPayout { get; }
    string RecipientBankCode { get; }
    string RecipientAccountNumber { get; }
    string RecipientAccountName { get; }
    DateTimeOffset TransferredAt { get; }
}
