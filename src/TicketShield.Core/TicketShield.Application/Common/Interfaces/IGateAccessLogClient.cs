using TicketShield.Application.Features.Disputes;

namespace TicketShield.Application.Common.Interfaces;

public interface IGateAccessLogClient
{
    Task<IReadOnlyList<GateScan>> GetScansAsync(string ticketCode, CancellationToken cancellationToken);
}
