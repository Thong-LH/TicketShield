using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MockOrganizer.API.Controllers;
using StackExchange.Redis;

namespace MockOrganizer.API.Services;

/// <summary>
/// Chặn replay checkout (SCRUM-247). Chạy trong controller trước Anti-Bot Guard,
/// nên không bị cơ chế Fail-Open 20ms bỏ qua. Nonce ghi bằng Redis SET NX (1 roundtrip),
/// dự phòng In-Memory khi Redis offline giống RedisSlidingWindowService.
/// </summary>
public class ReplayGuardService : IReplayGuardService
{
    private const int MaxNonceLength = 64;
    private const int MemorySweepThreshold = 10_000;
    private static readonly long MaxClockSkewMs = (long)TimeSpan.FromSeconds(5).TotalMilliseconds;
    private static readonly TimeSpan NonceTtl = TimeSpan.FromMinutes(10);

    private readonly ISessionTokenService _sessionTokens;
    private readonly IConnectionMultiplexer? _redis;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReplayGuardService>? _logger;
    private readonly string _prefix;

    // In-Memory Fallback Store: nonce key -> thời điểm hết hạn (epoch ms)
    private readonly ConcurrentDictionary<string, long> _memoryNonces = new();

    public ReplayGuardService(
        ISessionTokenService sessionTokens,
        IConnectionMultiplexer? redis = null,
        IConfiguration? config = null,
        TimeProvider? clock = null,
        ILogger<ReplayGuardService>? logger = null)
    {
        _sessionTokens = sessionTokens;
        _redis = redis;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
        _prefix = config?.GetValue<string>("Redis:InstanceName") ?? "ts:";
    }

    public async Task<ReplayGuardResult> ValidateAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        // 1. Token hợp lệ phải được cấp cho đúng hạng vé đang checkout.
        // Token thiếu hoặc sai chữ ký giữ nguyên luật Spec 08 (time_to_checkout = -1, vẫn chấm điểm).
        var (isTokenValid, _, tokenTierId) = _sessionTokens.ValidateSessionToken(request.SessionToken);
        if (isTokenValid && tokenTierId != request.TierId)
        {
            return ReplayGuardResult.Reject(
                ReplayRejectionCodes.TierMismatch,
                "Session token was issued for a different ticket tier.");
        }

        // 2. Client cũ hoặc bot gọi thẳng API không gửi nonce -> để mô hình chấm điểm như trước.
        var nonce = request.Telemetry?.Nonce;
        if (nonce is null)
            return ReplayGuardResult.Allowed;

        // 3. Nonce rỗng hoặc quá dài không thể là nonce hợp lệ của Client SDK.
        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length > MaxNonceLength)
            return ReplayGuardResult.Reject(ReplayRejectionCodes.Replay, "Invalid request nonce.");

        // 4. Giờ gửi lệch giờ server quá 5 giây theo cả hai chiều.
        // So sánh hai cận thay cho Math.Abs để giá trị cực đoan không ném OverflowException.
        var nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        var clientTimestamp = request.Telemetry!.ClientTimestamp;
        if (clientTimestamp is null)
            return ReplayGuardResult.Reject(ReplayRejectionCodes.ClockSkew, "Missing client timestamp.");

        var skewMs = nowMs - clientTimestamp.Value;
        if (skewMs > MaxClockSkewMs || skewMs < -MaxClockSkewMs)
            return ReplayGuardResult.Reject(ReplayRejectionCodes.ClockSkew, "Client timestamp is outside the allowed window.");

        // 5. Ghi nhận nonce một lần trong 10 phút. Ai ghi trước thì thắng.
        var claimed = await TryClaimNonceAsync($"{_prefix}nonce:{nonce}", nowMs);
        return claimed
            ? ReplayGuardResult.Allowed
            : ReplayGuardResult.Reject(ReplayRejectionCodes.Replay, "This request has already been submitted.");
    }

    private async Task<bool> TryClaimNonceAsync(string key, long nowMs)
    {
        if (IsRedisReady())
        {
            try
            {
                var db = _redis!.GetDatabase();
                return await db.StringSetAsync(key, "1", NonceTtl, When.NotExists, CommandFlags.None);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to claim nonce in Redis. Using in-memory fallback.");
            }
        }

        // In-memory fallback
        if (_memoryNonces.Count > MemorySweepThreshold)
            SweepExpiredNonces(nowMs);

        var expireAtMs = nowMs + (long)NonceTtl.TotalMilliseconds;
        while (true)
        {
            if (_memoryNonces.TryAdd(key, expireAtMs))
                return true;

            if (!_memoryNonces.TryGetValue(key, out var existingExpireAtMs))
                continue;

            if (existingExpireAtMs > nowMs)
                return false;

            // Nonce cũ đã hết hạn: chỉ một luồng thay được nhờ so sánh giá trị cũ.
            if (_memoryNonces.TryUpdate(key, expireAtMs, existingExpireAtMs))
                return true;
        }
    }

    private void SweepExpiredNonces(long nowMs)
    {
        foreach (var (key, expireAtMs) in _memoryNonces)
        {
            if (expireAtMs <= nowMs)
                _memoryNonces.TryRemove(new KeyValuePair<string, long>(key, expireAtMs));
        }
    }

    private bool IsRedisReady() => _redis != null && _redis.IsConnected;
}
