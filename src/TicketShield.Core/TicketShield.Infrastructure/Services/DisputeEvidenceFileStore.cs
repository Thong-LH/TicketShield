using TicketShield.Application.Common.Interfaces;

namespace TicketShield.Infrastructure.Services;

public class DisputeEvidenceFileStore : IDisputeEvidenceFileStore
{
    private readonly string _root;

    public DisputeEvidenceFileStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default)
    {
        var key = $"{Guid.NewGuid():N}.{extension.Trim().TrimStart('.').ToLowerInvariant()}";
        var path = Resolve(key);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return key;
    }

    public async Task<byte[]?> ReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        var name = Path.GetFileName(storageKey);
        if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, storageKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Khóa ảnh không hợp lệ.");
        }

        var full = Path.GetFullPath(Path.Combine(_root, name));
        var rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Khóa ảnh không hợp lệ.");
        }

        return full;
    }
}
