using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Dịch vụ theo dõi tương quan dải mạng Subnet /24 (IPv4) hoặc /48 (IPv6) (NET-13.1.3).
/// Phát hiện cụm Residential Proxy Botnet xoay vòng IP tấn công vào luồng checkout vé.
/// Hỗ trợ Redis ZSET Sliding Window nguyên tử trong 1 round-trip và In-Memory Fallback khi Redis offline.
/// Tự động miễn trừ (Exempt) Localhost và Private IP để không bao giờ gây false positive trong dev/test.
/// </summary>
public class SubnetCorrelationTracker : ISubnetCorrelationTracker
{
    // Lua Script nguyên tử: ZREMRANGEBYSCORE -> ZADD -> EXPIRE -> ZRANGE & distinct IP count
    private const string SubnetLuaScript = @"
local key = KEYS[1]
local now = tonumber(ARGV[1])
local window = tonumber(ARGV[2])
local cutoff = now - window
local member = ARGV[3]
local ttl = tonumber(ARGV[4])

redis.call('ZREMRANGEBYSCORE', key, '-inf', cutoff)
redis.call('ZADD', key, now, member)
redis.call('EXPIRE', key, ttl)

local members = redis.call('ZRANGE', key, 0, -1)
local total_count = #members

local distinct_ips = {}
local distinct_count = 0
for i, m in ipairs(members) do
    local first_colon = string.find(m, ':')
    if first_colon then
        local rest = string.sub(m, first_colon + 1)
        local second_colon = string.find(rest, ':')
        local ip = second_colon and string.sub(rest, 1, second_colon - 1) or rest
        if not distinct_ips[ip] then
            distinct_ips[ip] = true
            distinct_count = distinct_count + 1
        end
    end
end

return { total_count, distinct_count }
";

    private readonly IConnectionMultiplexer? _redis;
    private readonly TimeProvider _clock;
    private readonly ILogger<SubnetCorrelationTracker>? _logger;
    private readonly string _prefix;

    // Cửa sổ trượt và cấu hình ngưỡng
    private static readonly TimeSpan SlidingWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan KeyTtl = TimeSpan.FromSeconds(60);

    public const int SubnetBurstThreshold = 25;       // > 25 req / 10s trên cùng dải /24
    public const int ClusterBurstThreshold = 15;     // > 15 req / 10s trên cùng dải /24
    public const int ClusterDistinctIpThreshold = 5; // Có từ 5 IP khác nhau trở lên

    // Bộ đệm In-Memory Fallback (khi Redis offline hoặc chạy test độc lập)
    private readonly ConcurrentDictionary<string, List<(long Timestamp, string Ip)>> _memorySubnetStore = new();

    public SubnetCorrelationTracker(
        IConnectionMultiplexer? redis = null,
        IConfiguration? config = null,
        TimeProvider? clock = null,
        ILogger<SubnetCorrelationTracker>? logger = null)
    {
        _redis = redis;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
        _prefix = config?.GetValue<string>("Redis:InstanceName") ?? "ts:gw:";
    }

    public async Task<SubnetRateMetrics> TrackSubnetAsync(
        string? clientIp,
        CancellationToken ct = default)
    {
        var cleanIp = CleanIpString(clientIp);

        // 1. Kiểm tra IP Private / Loopback -> Miễn trừ kiểm tra (Exempt)
        if (IsPrivateOrLoopback(cleanIp))
        {
            return new SubnetRateMetrics
            {
                ClientIp = cleanIp,
                SubnetCidr = ExtractSubnetCidr(cleanIp),
                SubnetRequestCount = 1,
                DistinctIpCount = 1,
                IsExempt = true,
                IsAnomaly = false,
                InconsistencyScore = 0.0f,
                AnomalyReason = null
            };
        }

        var subnetCidr = ExtractSubnetCidr(cleanIp);
        var nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        int totalCount = 1;
        int distinctCount = 1;

        // 2. Thực thi qua Redis ZSET Sliding Window nếu khả dụng
        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var redisKey = (RedisKey)$"{_prefix}subnet:zset:{subnetCidr}";
                var member = $"{nowMs}:{cleanIp}:{Guid.NewGuid():N}";

                var result = await db.ScriptEvaluateAsync(
                    SubnetLuaScript,
                    new[] { redisKey },
                    new RedisValue[] { nowMs, (long)SlidingWindow.TotalMilliseconds, member, (long)KeyTtl.TotalSeconds });

                if (!result.IsNull)
                {
                    var array = (RedisResult[]?)result;
                    if (array != null && array.Length >= 2)
                    {
                        totalCount = (int)array[0];
                        distinctCount = (int)array[1];
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Redis subnet correlation script failed for {Subnet}. Falling back to in-memory store.", subnetCidr);
                (totalCount, distinctCount) = RecordToMemoryStore(subnetCidr, cleanIp, nowMs);
            }
        }
        else
        {
            // 3. Fallback sang In-Memory Store
            (totalCount, distinctCount) = RecordToMemoryStore(subnetCidr, cleanIp, nowMs);
        }

        // 4. Đánh giá phát hiện cụm botnet (Anomaly Evaluation)
        return EvaluateAnomaly(cleanIp, subnetCidr, totalCount, distinctCount);
    }

    private (int TotalCount, int DistinctCount) RecordToMemoryStore(string subnetCidr, string ip, long nowMs)
    {
        var cutoff = nowMs - (long)SlidingWindow.TotalMilliseconds;
        var list = _memorySubnetStore.GetOrAdd(subnetCidr, _ => new List<(long Timestamp, string Ip)>());

        lock (list)
        {
            list.RemoveAll(entry => entry.Timestamp < cutoff);
            list.Add((nowMs, ip));

            var total = list.Count;
            var distinct = list.Select(e => e.Ip).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return (total, distinct);
        }
    }

    private static SubnetRateMetrics EvaluateAnomaly(string clientIp, string subnetCidr, int totalCount, int distinctCount)
    {
        // Kịch bản A: Cụm Residential Proxy Botnet (Nhiều IP khác nhau trong cùng dải /24 gửi request dồn dập)
        if (totalCount >= ClusterBurstThreshold && distinctCount >= ClusterDistinctIpThreshold)
        {
            return new SubnetRateMetrics
            {
                ClientIp = clientIp,
                SubnetCidr = subnetCidr,
                SubnetRequestCount = totalCount,
                DistinctIpCount = distinctCount,
                IsExempt = false,
                IsAnomaly = true,
                InconsistencyScore = 1.0f,
                AnomalyReason = $"Residential proxy botnet detected: {distinctCount} distinct IPs executed {totalCount} checkout requests within 10s on subnet {subnetCidr}"
            };
        }

        // Kịch bản B: Dải Subnet bị spam request dồn dập (Burst Flood Attack)
        if (totalCount >= SubnetBurstThreshold)
        {
            return new SubnetRateMetrics
            {
                ClientIp = clientIp,
                SubnetCidr = subnetCidr,
                SubnetRequestCount = totalCount,
                DistinctIpCount = distinctCount,
                IsExempt = false,
                IsAnomaly = true,
                InconsistencyScore = 1.0f,
                AnomalyReason = $"Subnet burst attack detected: {totalCount} requests within 10s on subnet {subnetCidr}"
            };
        }

        // Lưu lượng bình thường
        return new SubnetRateMetrics
        {
            ClientIp = clientIp,
            SubnetCidr = subnetCidr,
            SubnetRequestCount = totalCount,
            DistinctIpCount = distinctCount,
            IsExempt = false,
            IsAnomaly = false,
            InconsistencyScore = 0.0f,
            AnomalyReason = null
        };
    }

    public string ExtractSubnetCidr(string? clientIp)
    {
        var cleanIp = CleanIpString(clientIp);
        if (string.IsNullOrWhiteSpace(cleanIp)) return "unknown/32";

        if (!IPAddress.TryParse(cleanIp, out var ip)) return $"{cleanIp}/32";

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            return $"{bytes[0]:x2}{bytes[1]:x2}:{bytes[2]:x2}{bytes[3]:x2}:{bytes[4]:x2}{bytes[5]:x2}::/48";
        }

        return $"{cleanIp}/32";
    }

    public bool IsPrivateOrLoopback(string? clientIp)
    {
        var cleanIp = CleanIpString(clientIp);
        if (string.IsNullOrWhiteSpace(cleanIp)) return true;

        if (!IPAddress.TryParse(cleanIp, out var ip)) return true;

        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 169.254.0.0/16 (Link Local)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal) return true;
        }

        return false;
    }

    private static string CleanIpString(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return string.Empty;

        var trimmed = ip.Trim();

        // Xử lý định dạng IPv6 có ngoặc vuông [::1]:5006
        if (trimmed.StartsWith('[') && trimmed.Contains(']'))
        {
            var closeIdx = trimmed.IndexOf(']');
            return trimmed.Substring(1, closeIdx - 1);
        }

        // Xử lý IPv4 có port "127.0.0.1:54321"
        if (trimmed.Contains(':') && trimmed.IndexOf(':') == trimmed.LastIndexOf(':'))
        {
            return trimmed.Split(':')[0];
        }

        return trimmed;
    }
}
