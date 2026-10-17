using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MockOrganizer.API.Services;

/// <summary>
/// Dịch vụ đếm tần suất và tương quan thiết bị qua Redis Sliding Window (BE-PIPE-8.2.2).
/// Hỗ trợ Lua Script tối ưu hóa 1 network roundtrip (&lt; 1.5ms) và In-Memory Fail-Open fallback.
/// </summary>
public class RedisSlidingWindowService : IRedisSlidingWindowService
{
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
    private static readonly LuaScript PreparedOverlapScript = LuaScript.Prepare(DeviceOverlapLuaScript);

    private readonly IConnectionMultiplexer? _redis;
    private readonly TimeProvider _clock;
    private readonly ILogger<RedisSlidingWindowService>? _logger;
    private readonly string _prefix;

    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BurstTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan OverlapWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan OverlapTtl = TimeSpan.FromHours(24);

    // In-Memory Fallback Store (Sử dụng khi Redis offline hoặc trong môi trường test độc lập)
    private readonly ConcurrentDictionary<string, List<long>> _memoryBurstStore = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, long>> _memoryOverlapStore = new();
    private readonly object _burstLock = new();

    public RedisSlidingWindowService(
        IConnectionMultiplexer? redis = null,
        IConfiguration? config = null,
        TimeProvider? clock = null,
        ILogger<RedisSlidingWindowService>? logger = null)
    {
        _redis = redis;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
        _prefix = config?.GetValue<string>("Redis:InstanceName") ?? "ts:";
    }

    public async Task<int> RecordAndGetBurstRateAsync(
        string? clientIp,
        string? deviceFingerprint,
        string? account,
        CancellationToken ct = default)
    {
        var nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        var keys = BuildBurstKeys(clientIp, deviceFingerprint, account);

        if (keys.Count == 0)
        {
            // Không có bất kỳ định danh nào -> request hiện tại tính là 1
            return 1;
        }

        if (IsRedisReady())
        {
            try
            {
                var db = _redis!.GetDatabase();
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
                _logger?.LogWarning(ex, "Redis burst rate counter failed. Falling back to in-memory sliding window.");
            }
        }

        return RecordAndGetBurstRateInMemory(keys, nowMs);
    }

    public async Task<int> RecordAndGetDeviceAccountOverlapAsync(
        string? deviceFingerprint,
        string? account,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceFingerprint))
        {
            // Spec 08: Missing device_fingerprint -> -1
            return -1;
        }

        var normalizedDevice = deviceFingerprint.Trim();
        var normalizedAccount = string.IsNullOrWhiteSpace(account) ? string.Empty : account.Trim();
        var nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        var overlapKey = $"{_prefix}dev:accounts:{normalizedDevice}";

        if (IsRedisReady())
        {
            try
            {
                var db = _redis!.GetDatabase();
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
                _logger?.LogWarning(ex, "Redis device overlap counter failed for {Device}. Falling back to in-memory store.", normalizedDevice);
            }
        }

        return RecordAndGetDeviceOverlapInMemory(normalizedDevice, normalizedAccount, nowMs);
    }

    public async Task<RedisSlidingWindowTelemetryResult> MeasureSlidingWindowTelemetryAsync(
        string? clientIp,
        string? deviceFingerprint,
        string? account,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var isRedis = IsRedisReady();

        var burstTask = RecordAndGetBurstRateAsync(clientIp, deviceFingerprint, account, ct);
        var overlapTask = RecordAndGetDeviceAccountOverlapAsync(deviceFingerprint, account, ct);

        await Task.WhenAll(burstTask, overlapTask);

        sw.Stop();
        var durationMs = sw.Elapsed.TotalMilliseconds;

        return new RedisSlidingWindowTelemetryResult(
            BurstRate10s: await burstTask,
            DeviceAccountOverlap: await overlapTask,
            ExecutionDurationMs: Math.Round(durationMs, 3),
            IsRedisAvailable: isRedis
        );
    }

    private bool IsRedisReady() => _redis != null && _redis.IsConnected;

    private List<string> BuildBurstKeys(string? ip, string? dev, string? acc)
    {
        var keys = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(ip))
            keys.Add($"{_prefix}rate:burst:10s:ip:{ip.Trim()}");

        if (!string.IsNullOrWhiteSpace(dev))
            keys.Add($"{_prefix}rate:burst:10s:dev:{dev.Trim()}");

        // Khi đã có thông tin tài khoản/email, bổ sung chiều kiểm tra theo tài khoản
        if (!string.IsNullOrWhiteSpace(acc))
            keys.Add($"{_prefix}rate:burst:10s:acc:{acc.Trim().ToLowerInvariant()}");

        return keys;
    }

    private int RecordAndGetBurstRateInMemory(List<string> keys, long nowMs)
    {
        var cutoff = nowMs - (long)BurstWindow.TotalMilliseconds;
        var maxCount = 1;

        lock (_burstLock)
        {
            foreach (var key in keys)
            {
                var list = _memoryBurstStore.GetOrAdd(key, _ => new List<long>());
                list.RemoveAll(ts => ts <= cutoff);
                list.Add(nowMs);
                if (list.Count > maxCount)
                {
                    maxCount = list.Count;
                }
            }
        }

        return maxCount;
    }

    private int RecordAndGetDeviceOverlapInMemory(string device, string account, long nowMs)
    {
        var cutoff = nowMs - (long)OverlapWindow.TotalMilliseconds;
        var accounts = _memoryOverlapStore.GetOrAdd(device, _ => new ConcurrentDictionary<string, long>());

        // 1. Dọn dẹp tài khoản quá 24h
        foreach (var (acc, lastSeen) in accounts)
        {
            if (lastSeen <= cutoff)
            {
                accounts.TryRemove(acc, out _);
            }
        }

        // 2. Cập nhật tài khoản hiện tại
        if (!string.IsNullOrWhiteSpace(account))
        {
            accounts[account.Trim().ToLowerInvariant()] = nowMs;
        }

        return accounts.Count;
    }
}
