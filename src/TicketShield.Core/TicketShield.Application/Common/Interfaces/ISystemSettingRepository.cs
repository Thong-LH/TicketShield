using TicketShield.Application.Features.Admin.FeeSettings.Models;

namespace TicketShield.Application.Common.Interfaces;

public interface ISystemSettingRepository
{
    Task<ResaleFeeConfig> GetResaleFeeConfigAsync(CancellationToken ct = default);
    Task UpdateResaleFeeConfigAsync(ResaleFeeConfig config, Guid? updatedBy, CancellationToken ct = default);
}
