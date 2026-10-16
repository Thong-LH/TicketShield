using System.Collections.Concurrent;

namespace MockOrganizer.API.Services;

/// <summary>
/// Triển khai In-Memory chuẩn bị cho Redis của IPipelineHoldTracker.
/// Quản lý key "hold:{buyerEmail}" với TTL 10 phút, hỗ trợ đọc ghi < 0.1ms cho Pipeline Flash-sale.
/// </summary>
public class InMemoryPipelineHoldTracker(TimeProvider? clock = null) : IPipelineHoldTracker
{
    private record CacheEntry(int Count, DateTimeOffset ExpiresAtUtc);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public Task<int> GetHoldCountAsync(string buyerEmail, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buyerEmail))
            return Task.FromResult(0);

        var key = NormalizeKey(buyerEmail);
        var now = _clock.GetUtcNow();

        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAtUtc > now)
            {
                return Task.FromResult(Math.Max(0, entry.Count));
            }

            // Đã hết hạn -> Dọn dẹp key
            _cache.TryRemove(key, out _);
        }

        return Task.FromResult(0);
    }

    public Task SetHoldCountAsync(string buyerEmail, int count, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buyerEmail))
            return Task.CompletedTask;

        var key = NormalizeKey(buyerEmail);
        var expiry = _clock.GetUtcNow().Add(ttl ?? DefaultTtl);

        _cache[key] = new CacheEntry(Math.Max(0, count), expiry);
        return Task.CompletedTask;
    }

    public Task<int> IncrementHoldCountAsync(string buyerEmail, int delta, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buyerEmail))
            return Task.FromResult(0);

        var key = NormalizeKey(buyerEmail);
        var now = _clock.GetUtcNow();
        var expiry = now.Add(ttl ?? DefaultTtl);

        var updatedEntry = _cache.AddOrUpdate(
            key,
            _ => new CacheEntry(Math.Max(0, delta), expiry),
            (_, existing) =>
            {
                var effectiveCount = existing.ExpiresAtUtc > now ? existing.Count : 0;
                var newCount = Math.Max(0, effectiveCount + delta);
                return new CacheEntry(newCount, expiry);
            });

        return Task.FromResult(updatedEntry.Count);
    }

    private static string NormalizeKey(string buyerEmail) => $"hold:{buyerEmail.Trim().ToLowerInvariant()}";
}
