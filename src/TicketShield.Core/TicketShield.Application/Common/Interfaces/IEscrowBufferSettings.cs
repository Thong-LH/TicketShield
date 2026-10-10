using TicketShield.Application.Features.Admin.EscrowBuffer.Models;

namespace TicketShield.Application.Common.Interfaces;

public interface IEscrowBufferSettings
{
    Task<EscrowBufferConfig> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(EscrowBufferConfig config, Guid? updatedBy, CancellationToken ct = default);
    void InvalidateCache();
}
