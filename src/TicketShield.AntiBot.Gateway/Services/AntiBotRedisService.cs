using System.Collections.Concurrent;
using System.Diagnostics;
using StackExchange.Redis;

namespace TicketShield.AntiBot.Gateway.Services;

public record RedisTelemetryResult(
    float BurstRate10s,
    float ConcurrentHold,
    float DeviceAccountOverlap,
    double DurationMs,
    bool IsRedisAvailable
);

public interface IAntiBotRedisService
{
    Task<RedisTelemetryResult> MeasureTelemetryAsync(
        string? clientIp,
        string? deviceFingerprint,
        string? buyerEmail,
        CancellationToken ct = default);
}

/// <summary>
/// Dịch vụ Redis phòng thủ tại Gateway (BE-GW-8.6.4 - SCRUM-273).
/// Triển khai Đếm lạc quan nguyên tử (Atomic Optimistic Hold Tracking) qua Lua Script trên Redis ZSET,
/// triệt tiêu hoàn toàn race condition khi bot gửi hàng loạt request song song.
/// </summary>
public class AntiBotRedisService : IAntiBotRedisService
{
    // Lua Script 1: Burst Rate 10s (Sliding Window ZSET)
    private const string BurstRateLuaScript = @"
local now = tonumber(ARGV[1])
local window = tonumber(ARGV[2])
local cutoff = now - window
local member = ARGV[3]
local ttl = tonumber(ARGV[4])

local max_count = 1
for i, key in ipairs(KEYS) do
    redis.call('ZREMRANGEBYSCORE', key, '-inf', cutoff)
    redis.call('ZADD', key, now, member)
    local count = redis.call('ZCARD', key)
    redis.call('EXPIRE', key, ttl)
    if count > max_count then
        max_count = count
    end
end
return max_count
";

    // Lua Script 2: Atomic Optimistic Hold Tracking qua Redis ZSET (SCRUM-273 / Spec 08)
    // Nguyên tử trong 1 roundtrip: Xóa hold cũ quá hạn (600s) -> Đọc số hold hiện tại -> Ghi hold mới
    private const string AtomicHoldLuaScript = @"
local key = KEYS[1]
local now = tonumber(ARGV[1])
local cutoff = now - tonumber(ARGV[2])
local member = ARGV[3]
local ttl = tonumber(ARGV[4])

redis.call('ZREMRANGEBYSCORE', key, '-inf', cutoff)
local current_hold = redis.call('ZCARD', key)
redis.call('ZADD', key, now, member)
redis.call('EXPIRE', key, ttl)
return current_hold
";

    // Lua Script 3: Device Account Overlap (Redis Set / ZSET 24h sliding)
    private const string DeviceOverlapLuaScript = @"
local key = KEYS[1]
local now = tonumber(ARGV[1])
local cutoff = now - tonumber(ARGV[2])
local account = ARGV[3]
local ttl = tonumber(ARGV[4])

redis.call('ZREMRANGEBYSCORE', key, '-inf', cutoff)
if account and account ~= '' then
    redis.call('ZADD', key, now, account)
end
local count = redis.call('ZCARD', key)
redis.call('EXPIRE', key, ttl)
return count
";

    private static readonly LuaScript PreparedBurstScript = LuaScript.Prepare(BurstRateLuaScript);
    private static readonly LuaScript PreparedHoldScript = LuaScript.Prepare(AtomicHoldLuaScript);
    private static readonly LuaScript PreparedOverlapScript = LuaScript.Prepare(DeviceOverlapLuaScript);

    private readonly IConnectionMultiplexer? _redis;
    private readonly TimeProvider _clock;
    private readonly ILogger<AntiBotRedisService>? _logger;
    private readonly string _prefix;

    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BurstTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HoldWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan HoldTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OverlapWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan OverlapTtl = TimeSpan.FromHours(24);

    // In-Memory Fallback Stores (bảo đảm SLA < 2ms khi Redis offline)
    private readonly ConcurrentDictionary<string, List<long>> _memoryBurstStore = new();
    private readonly ConcurrentDictionary<string, List<long>> _memoryHoldStore = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, long>> _memoryOverlapStore = new();

    public AntiBotRedisService(
        IConnectionMultiplexer? redis = null,
        IConfiguration? config = null,
        TimeProvider? clock = null,
        ILogger<AntiBotRedisService>? logger = null)
    {
        _redis = redis;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
        _prefix = config?.GetValue<string>("Redis:InstanceName") ?? "ts:gw:";
    }

    public async Task<RedisTelemetryResult> MeasureTelemetryAsync(
        string? clientIp,
        string? deviceFingerprint,
        string? buyerEmail,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var isRedis = _redis != null && _redis.IsConnected;

        var burstTask = MeasureBurstRateAsync(clientIp, deviceFingerprint, buyerEmail);
        var holdTask = MeasureAtomicHoldAsync(buyerEmail);
        var overlapTask = MeasureDeviceOverlapAsync(deviceFingerprint, buyerEmail);

        await Task.WhenAll(burstTask, holdTask, overlapTask);

        sw.Stop();
        return new RedisTelemetryResult(
            BurstRate10s: await burstTask,
            ConcurrentHold: await holdTask,
            DeviceAccountOverlap: await overlapTask,
            DurationMs: Math.Round(sw.Elapsed.TotalMilliseconds, 3),
            IsRedisAvailable: isRedis
        );
    }

    private async Task<float> MeasureBurstRateAsync(string? clientIp, string? deviceFingerprint, string? account)
    {
        var nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(clientIp)) keys.Add($"{_prefix}burst:ip:{clientIp.Trim()}");
        if (!string.IsNullOrWhiteSpace(deviceFingerprint)) keys.Add($"{_prefix}burst:dev:{deviceFingerprint.Trim()}");
        if (!string.IsNullOrWhiteSpace(account)) keys.Add($"{_prefix}burst:acc:{account.Trim().ToLowerInvariant()}");

        if (keys.Count == 0) return 1.0f;

        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
                var member = $"{nowMs}:{Guid.NewGuid():N}";
                var result = await db.ScriptEvaluateAsync(PreparedBurstScript, new
                {
                    now = nowMs,
                    window = (long)BurstWindow.TotalMilliseconds,
                    member = (RedisValue)member,
                    ttl = (long)BurstTtl.TotalSeconds
                });
                return (int)result;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Redis burst script failed. Using memory fallback.");
            }
        }

        // In-memory fallback
        int maxCount = 1;
        var cutoff = nowMs - (long)BurstWindow.TotalMilliseconds;
        foreach (var key in keys)
        {
            var list = _memoryBurstStore.GetOrAdd(key, _ => new List<long>());
            lock (list)
            {
                list.RemoveAll(ts => ts < cutoff);
                list.Add(nowMs);
                if (list.Count > maxCount) maxCount = list.Count;
            }
        }
        return maxCount;
    }

    private async Task<float> MeasureAtomicHoldAsync(string? buyerEmail)
    {
        if (string.IsNullOrWhiteSpace(buyerEmail)) return 0.0f;

        var key = $"{_prefix}hold:zset:{buyerEmail.Trim().ToLowerInvariant()}";
        var nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();

        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var member = $"{nowMs}:{Guid.NewGuid():N}";
                var result = await db.ScriptEvaluateAsync(PreparedHoldScript, new
                {
                    now = nowMs,
                    window = (long)HoldWindow.TotalMilliseconds,
                    member = (RedisValue)member,
                    ttl = (long)HoldTtl.TotalSeconds
                });
                return (int)result;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Redis atomic hold script failed. Using memory fallback.");
            }
        }

        // In-memory atomic hold fallback
        var list = _memoryHoldStore.GetOrAdd(key, _ => new List<long>());
        var cutoff = nowMs - (long)HoldWindow.TotalMilliseconds;
        lock (list)
        {
            list.RemoveAll(ts => ts < cutoff);
            var currentCount = list.Count;
            list.Add(nowMs);
            return currentCount;
        }
    }

    private async Task<float> MeasureDeviceOverlapAsync(string? deviceFingerprint, string? account)
    {
        if (string.IsNullOrWhiteSpace(deviceFingerprint)) return -1.0f;

        var normalizedDevice = deviceFingerprint.Trim();
        var normalizedAccount = string.IsNullOrWhiteSpace(account) ? string.Empty : account.Trim().ToLowerInvariant();
        var key = $"{_prefix}dev:overlap:{normalizedDevice}";
        var nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();

        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var result = await db.ScriptEvaluateAsync(PreparedOverlapScript, new
                {
                    now = nowMs,
                    window = (long)OverlapWindow.TotalMilliseconds,
                    account = (RedisValue)normalizedAccount,
                    ttl = (long)OverlapTtl.TotalSeconds
                });
                return (int)result;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Redis device overlap script failed. Using memory fallback.");
            }
        }

        // In-memory fallback
        var dict = _memoryOverlapStore.GetOrAdd(key, _ => new ConcurrentDictionary<string, long>());
        var cutoff = nowMs - (long)OverlapWindow.TotalMilliseconds;
        foreach (var pair in dict)
        {
            if (pair.Value < cutoff) dict.TryRemove(pair.Key, out _);
        }
        if (!string.IsNullOrEmpty(normalizedAccount))
        {
            dict[normalizedAccount] = nowMs;
        }
        return dict.Count;
    }
}
