namespace TicketShield.Application.Common.Interfaces;

public interface IDisputeEvidenceFileStore
{
    Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default);

    Task<byte[]?> ReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
