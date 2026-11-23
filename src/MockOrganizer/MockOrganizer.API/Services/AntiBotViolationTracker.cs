using System.Collections.Concurrent;
using StackExchange.Redis;

namespace MockOrganizer.API.Services;

/// <summary>
/// Dịch vụ theo dõi bộ đếm vi phạm thống nhất và thang Cooldown lũy tiến (SCRUM-403 - Technical Memo v5).
/// Tích hợp Redis Sliding Window TTL 30 phút và cơ chế Fallback In-Memory khi Redis offline.
/// </summary>
public class AntiBotViolationTracker : IAntiBotViolationTracker
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly TimeProvider _clock;
    private readonly ILogger<AntiBotViolationTracker>? _logger;
    private readonly string _prefix;

    private static readonly TimeSpan SlidingWindowTtl = TimeSpan.FromMinutes(30);

    // In-memory fallback
    private readonly ConcurrentDictionary<string, MemoryViolationEntry> _memoryStore = new();

    private sealed record MemoryViolationEntry(int Score, long CooldownUntilMs, long ExpireAtMs);

    public AntiBotViolationTracker(
        IConnectionMultiplexer? redis = null,
        IConfiguration? config = null,
        TimeProvider? clock = null,
        ILogger<AntiBotViolationTracker>? logger = null)
    {
        _redis = redis;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
        _prefix = config?.GetValue<string>("Redis:InstanceName") ?? "ts:";
    }

    public async Task<ViolationStatus> GetViolationStatusAsync(
        string? sessionToken,
        string? deviceFingerprint,
        string? clientIp,
        CancellationToken ct = default)
    {
        var key = BuildKey(sessionToken, deviceFingerprint, clientIp);
        if (string.IsNullOrEmpty(key))
        {
            return new ViolationStatus { Score = 0, CooldownSeconds = 0, IsHardCutoff = false, RetryAfterSeconds = 0 };
        }

        var nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();

        if (IsRedisReady())
        {
            try
            {
                var db = _redis!.GetDatabase();
                var scoreKey = $"{_prefix}antibot:violation:{key}";
                var cdKey = $"{_prefix}antibot:cooldown:{key}";

                var scoreVal = await db.StringGetAsync(scoreKey);
                var cdVal = await db.StringGetAsync(cdKey);

                var score = scoreVal.HasValue && int.TryParse(scoreVal, out var s) ? s : 0;
                long cooldownUntilMs = cdVal.HasValue && long.TryParse(cdVal, out var cd) ? cd : 0;

                return CalculateStatus(score, cooldownUntilMs, nowMs);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AntiBotViolationTracker: Lỗi truy vấn Redis. Sử dụng In-Memory fallback.");
            }
        }

        // In-memory fallback
        if (_memoryStore.TryGetValue(key, out var entry))
        {
            if (nowMs > entry.ExpireAtMs)
            {
                _memoryStore.TryRemove(key, out _);
                return new ViolationStatus { Score = 0, CooldownSeconds = 0, IsHardCutoff = false, RetryAfterSeconds = 0 };
            }
            return CalculateStatus(entry.Score, entry.CooldownUntilMs, nowMs);
        }

        return new ViolationStatus { Score = 0, CooldownSeconds = 0, IsHardCutoff = false, RetryAfterSeconds = 0 };
    }

    public async Task<ViolationStatus> RecordViolationAsync(
        string? sessionToken,
        string? deviceFingerprint,
        string? clientIp,
        int points,
        CancellationToken ct = default)
    {
        var key = BuildKey(sessionToken, deviceFingerprint, clientIp);
        if (string.IsNullOrEmpty(key) || points <= 0)
        {
            return new ViolationStatus { Score = 0, CooldownSeconds = 0, IsHardCutoff = false, RetryAfterSeconds = 0 };
        }

        var nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();

        if (IsRedisReady())
        {
            try
            {
                var db = _redis!.GetDatabase();
                var scoreKey = $"{_prefix}antibot:violation:{key}";
                var cdKey = $"{_prefix}antibot:cooldown:{key}";

                var newScore = (int)await db.StringIncrementAsync(scoreKey, points);
                await db.KeyExpireAsync(scoreKey, SlidingWindowTtl);

                var cooldownSec = GetCooldownDurationSeconds(newScore);
                var cooldownUntilMs = nowMs + (cooldownSec * 1000L);

                await db.StringSetAsync(cdKey, cooldownUntilMs, TimeSpan.FromSeconds(cooldownSec));

                _logger?.LogInformation(
                    "AntiBotViolationTracker: Ghi nhận vi phạm +{Points}đ cho {Key}. Tổng điểm: {Score}, Khóa: {Cd}s",
                    points, key, newScore, cooldownSec);

                return CalculateStatus(newScore, cooldownUntilMs, nowMs);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AntiBotViolationTracker: Lỗi ghi Redis. Sử dụng In-Memory fallback.");
            }
        }

        // In-memory fallback
        var updated = _memoryStore.AddOrUpdate(key,
            _ =>
            {
                var cd = GetCooldownDurationSeconds(points);
                return new MemoryViolationEntry(points, nowMs + (cd * 1000L), nowMs + (long)SlidingWindowTtl.TotalMilliseconds);
            },
            (_, existing) =>
            {
                var currentScore = nowMs > existing.ExpireAtMs ? 0 : existing.Score;
                var nextScore = currentScore + points;
                var cd = GetCooldownDurationSeconds(nextScore);
                return new MemoryViolationEntry(nextScore, nowMs + (cd * 1000L), nowMs + (long)SlidingWindowTtl.TotalMilliseconds);
            });

        return CalculateStatus(updated.Score, updated.CooldownUntilMs, nowMs);
    }

    public async Task ResetViolationAsync(
        string? sessionToken,
        string? deviceFingerprint,
        string? clientIp,
        CancellationToken ct = default)
    {
        var key = BuildKey(sessionToken, deviceFingerprint, clientIp);
        if (string.IsNullOrEmpty(key)) return;

        if (IsRedisReady())
        {
            try
            {
                var db = _redis!.GetDatabase();
                await db.KeyDeleteAsync([$"{_prefix}antibot:violation:{key}", $"{_prefix}antibot:cooldown:{key}"]);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AntiBotViolationTracker: Lỗi xóa key Redis.");
            }
        }

        _memoryStore.TryRemove(key, out _);
    }

    private static int GetCooldownDurationSeconds(int score) => score switch
    {
        <= 0 => 0,
        1 => 5,
        2 => 15,
        3 or 4 => 60,
        _ => 900 // >= 5: Hard Cutoff khóa 15 phút (900s)
    };

    private static ViolationStatus CalculateStatus(int score, long cooldownUntilMs, long nowMs)
    {
        var cooldownSec = GetCooldownDurationSeconds(score);
        var remainingMs = Math.Max(0, cooldownUntilMs - nowMs);
        var retryAfterSec = (long)Math.Ceiling(remainingMs / 1000.0);

        return new ViolationStatus
        {
            Score = score,
            CooldownSeconds = cooldownSec,
            IsHardCutoff = score >= 5,
            RetryAfterSeconds = retryAfterSec
        };
    }

    private static string BuildKey(string? sessionToken, string? deviceFingerprint, string? clientIp)
    {
        if (!string.IsNullOrWhiteSpace(sessionToken) && !string.IsNullOrWhiteSpace(deviceFingerprint))
        {
            return $"{sessionToken}:{deviceFingerprint}";
        }
        if (!string.IsNullOrWhiteSpace(deviceFingerprint))
        {
            return $"{deviceFingerprint}:{clientIp ?? "unknown"}";
        }
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            return $"{sessionToken}:{clientIp ?? "unknown"}";
        }
        return !string.IsNullOrWhiteSpace(clientIp) ? clientIp : string.Empty;
    }

    private bool IsRedisReady() => _redis != null && _redis.IsConnected;
}
