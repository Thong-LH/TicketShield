using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Common.Interfaces;

public interface IPayoutGateway
{
    Task<PayoutGatewayResult> TransferAsync(PayoutTransferRequest request, CancellationToken cancellationToken = default);
}
