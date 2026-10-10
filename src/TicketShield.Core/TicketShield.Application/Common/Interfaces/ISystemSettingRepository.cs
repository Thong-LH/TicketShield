using TicketShield.Application.Features.Admin.EscrowBuffer.Models;
using TicketShield.Application.Features.Admin.FeeSettings.Models;

namespace TicketShield.Application.Common.Interfaces;

public interface ISystemSettingRepository
{
    Task<ResaleFeeConfig> GetResaleFeeConfigAsync(CancellationToken ct = default);
    Task UpdateResaleFeeConfigAsync(ResaleFeeConfig config, Guid? updatedBy, CancellationToken ct = default);
    Task<EscrowBufferConfig> GetEscrowBufferConfigAsync(CancellationToken ct = default);
    Task UpdateEscrowBufferConfigAsync(EscrowBufferConfig config, Guid? updatedBy, CancellationToken ct = default);
}
