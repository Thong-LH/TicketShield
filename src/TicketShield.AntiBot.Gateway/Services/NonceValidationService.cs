using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TicketShield.AntiBot.Gateway.Models;

namespace TicketShield.AntiBot.Gateway.Services;

/// <summary>
/// Dịch vụ quản lý và xác thực One-Time Cryptographic Nonce tại Gateway (US-13.2.3, SCRUM-376).
/// Triển khai Atomic Check-and-Consume qua Lua Script trên Redis (prefix ts:gw:nonce:),
/// kèm In-Memory Fallback tự phục hồi khi Redis offline.
/// </summary>
public class NonceValidationService : INonceValidationService
{
    private const int MemorySweepThreshold = 10_000;
    private static readonly TimeSpan DefaultConsumedTtl = TimeSpan.FromMinutes(5);

    // Lua Script kiểm tra và tiêu thụ Nonce nguyên tử trong 1 round-trip duy nhất:
    // Return codes:
    // 0: Key không tồn tại hoặc đã quá hạn
    // 1: Key đã ở trạng thái 'consumed' (Replay Attack)
    // 2: TierId không khớp với lúc cấp phát
    // 3: Thành công -> Đổi trạng thái sang 'consumed' và giữ lại TTL để chặn replay
    private const string ConsumeNonceLuaScript = @"
local key = KEYS[1]
local expectedTier = ARGV[1]
local consumedTtl = tonumber(ARGV[2])

local val = redis.call('GET', key)
if not val then
    return 0
end

if val == 'consumed' then
    return 1
end

local prefix = string.sub(val, 1, 7)
if prefix == 'issued:' then
    local savedTier = string.sub(val, 8)
    if expectedTier ~= '' and savedTier ~= '' and savedTier ~= expectedTier then
        return 2
    end
    redis.call('SET', key, 'consumed', 'EX', consumedTtl)
    return 3
end

if val == 'issued' then
    redis.call('SET', key, 'consumed', 'EX', consumedTtl)
    return 3
end

return 0
";

    private static readonly LuaScript PreparedConsumeScript = LuaScript.Prepare(ConsumeNonceLuaScript);

    private readonly IConnectionMultiplexer? _redis;
    private readonly TimeProvider _clock;
    private readonly ILogger<NonceValidationService>? _logger;
    private readonly string _prefix;

    private record NonceEntry(string Status, Guid TierId, long ExpiryEpochMs);

    // In-Memory Fallback Store: key -> NonceEntry
    private readonly ConcurrentDictionary<string, NonceEntry> _memoryStore = new();

    public NonceValidationService(
        IConnectionMultiplexer? redis = null,
        IConfiguration? config = null,
        TimeProvider? clock = null,
        ILogger<NonceValidationService>? logger = null)
    {
        _redis = redis;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
        _prefix = config?.GetValue<string>("Redis:InstanceName") ?? "ts:gw:";
    }

    public async Task RegisterNonceAsync(string nonce, Guid tierId, TimeSpan ttl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nonce))
            return;

        var key = BuildKey(nonce);
        var nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        var expiryEpochMs = nowMs + (long)ttl.TotalMilliseconds;
        var value = $"issued:{tierId}";

        // 1. Luôn ghi nhận vào In-Memory Store để sẵn sàng dự phòng khi Redis rớt mạng
        _memoryStore[key] = new NonceEntry("issued", tierId, expiryEpochMs);

        // 2. Ghi nhận vào Redis nếu kết nối sẵn sàng
        if (IsRedisReady())
        {
            try
            {
                var db = _redis!.GetDatabase();
                await db.StringSetAsync(key, value, ttl);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to register nonce in Redis for key {Key}. In-memory fallback will be used.", key);
            }
        }

        // Định kỳ quét dọn In-Memory Store nếu dung lượng vượt ngưỡng
        if (_memoryStore.Count > MemorySweepThreshold)
        {
            SweepExpiredNonces(nowMs);
        }
    }

    public async Task<NonceConsumptionResult> ValidateAndConsumeNonceAsync(string nonce, Guid tierId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nonce))
        {
            return NonceConsumptionResult.Expired("Invalid or missing request nonce.");
        }

        var key = BuildKey(nonce);
        var nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();

        // 1. Thử xác thực nguyên tử qua Redis
        if (IsRedisReady())
        {
            try
            {
                var db = _redis!.GetDatabase();
                var expectedTierStr = tierId != Guid.Empty ? tierId.ToString() : string.Empty;
                var consumedTtlSeconds = (int)DefaultConsumedTtl.TotalSeconds;

                var result = (int)await db.ScriptEvaluateAsync(
                    PreparedConsumeScript,
                    new { key = (RedisKey)key, expectedTier = expectedTierStr, consumedTtl = consumedTtlSeconds });

                return result switch
                {
                    3 => NonceConsumptionResult.Success(),
                    1 => NonceConsumptionResult.Replay("Nonce has already been consumed (replay attack detected)."),
                    2 => NonceConsumptionResult.Failed("Nonce was registered for a different ticket tier."),
                    _ => NonceConsumptionResult.Expired("Nonce not found or expired.")
                };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to validate nonce via Redis for key {Key}. Falling back to in-memory store.", key);
            }
        }

        // 2. In-Memory Fallback khi Redis không khả dụng hoặc lỗi mạng
        if (!_memoryStore.TryGetValue(key, out var entry))
        {
            return NonceConsumptionResult.Expired("Nonce not found or expired.");
        }

        if (entry.ExpiryEpochMs < nowMs)
        {
            _memoryStore.TryRemove(key, out _);
            return NonceConsumptionResult.Expired("Nonce has expired.");
        }

        if (entry.Status == "consumed")
        {
            return NonceConsumptionResult.Replay("Nonce has already been consumed (replay attack detected).");
        }

        if (tierId != Guid.Empty && entry.TierId != Guid.Empty && entry.TierId != tierId)
        {
            return NonceConsumptionResult.Failed("Nonce was registered for a different ticket tier.");
        }

        // Atomic update In-Memory
        bool wasConsumed = false;
        _memoryStore.AddOrUpdate(
            key,
            new NonceEntry("consumed", entry.TierId, nowMs + (long)DefaultConsumedTtl.TotalMilliseconds),
            (_, existing) =>
            {
                if (existing.Status == "consumed")
                {
                    return existing;
                }
                wasConsumed = true;
                return existing with { Status = "consumed", ExpiryEpochMs = nowMs + (long)DefaultConsumedTtl.TotalMilliseconds };
            });

        return wasConsumed
            ? NonceConsumptionResult.Success()
            : NonceConsumptionResult.Replay("Nonce has already been consumed (replay attack detected).");
    }

    private string BuildKey(string nonce) => $"{_prefix}nonce:{nonce}";

    private bool IsRedisReady() => _redis != null && _redis.IsConnected;

    private void SweepExpiredNonces(long nowMs)
    {
        foreach (var (k, v) in _memoryStore)
        {
            if (v.ExpiryEpochMs < nowMs)
            {
                _memoryStore.TryRemove(k, out _);
            }
        }
    }
}
