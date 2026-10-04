namespace TicketShield.Application.Common.Interfaces;

public interface IPayoutReportApplier
{
    Task<bool> ApplyAsync(Guid escrowId, bool succeeded, string? bankReference, CancellationToken cancellationToken = default);
}
