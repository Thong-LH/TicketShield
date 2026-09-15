using TicketShield.Application.Features.Admin.FeeSettings.Models;

namespace TicketShield.Application.Common.Interfaces;

public interface IResaleFeeCalculator
{
    Task<ResaleFeeCalculationResult> CalculateFeeAsync(decimal resalePrice, bool isPrivate, CancellationToken ct = default);
    void InvalidateCache();
}
