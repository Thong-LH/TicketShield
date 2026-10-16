using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace MockOrganizer.API.Services;

public class SessionTokenService : ISessionTokenService
{
    private readonly byte[] _keyBytes;
    private readonly TimeProvider _clock;

    public SessionTokenService(IConfiguration config, TimeProvider? clock = null)
    {
        var secret = config["SessionToken:SecretKey"]
                     ?? "TicketShield_MockOrganizer_AntiBot_SecretKey_2026_Secure!";
        _keyBytes = Encoding.UTF8.GetBytes(secret);
        _clock = clock ?? TimeProvider.System;
    }

    public string GenerateSessionToken(Guid tierId, DateTimeOffset? issuedAt = null)
    {
        var now = issuedAt ?? _clock.GetUtcNow();
        var epochMs = now.ToUnixTimeMilliseconds();
        var sessionId = Guid.NewGuid();
        var payload = $"{sessionId:N}:{tierId:D}:{epochMs}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var payloadBase64 = Convert.ToBase64String(payloadBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        using var hmac = new HMACSHA256(_keyBytes);
        var signatureBytes = hmac.ComputeHash(payloadBytes);
        var signatureBase64 = Convert.ToBase64String(signatureBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{payloadBase64}.{signatureBase64}";
    }

    public (bool IsValid, DateTimeOffset IssuedAt, Guid TierId) ValidateSessionToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (false, DateTimeOffset.MinValue, Guid.Empty);

        var parts = token.Split('.');
        if (parts.Length != 2)
            return (false, DateTimeOffset.MinValue, Guid.Empty);

        try
        {
            var payloadBase64 = PadBase64(parts[0].Replace('-', '+').Replace('_', '/'));
            var payloadBytes = Convert.FromBase64String(payloadBase64);

            using var hmac = new HMACSHA256(_keyBytes);
            var expectedSignature = hmac.ComputeHash(payloadBytes);

            var actualSignatureBase64 = PadBase64(parts[1].Replace('-', '+').Replace('_', '/'));
            var actualSignature = Convert.FromBase64String(actualSignatureBase64);

            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
                return (false, DateTimeOffset.MinValue, Guid.Empty);

            var payload = Encoding.UTF8.GetString(payloadBytes);
            var segments = payload.Split(':');
            if (segments.Length != 3)
                return (false, DateTimeOffset.MinValue, Guid.Empty);

            if (!Guid.TryParse(segments[0], out _) ||
                !Guid.TryParse(segments[1], out var tierId) ||
                !long.TryParse(segments[2], out var epochMs))
            {
                return (false, DateTimeOffset.MinValue, Guid.Empty);
            }

            var issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
            var now = _clock.GetUtcNow();

            // Chặn token trong tương lai (cho phép dung sai clock skew 5 giây)
            if (issuedAt > now.AddSeconds(5))
                return (false, DateTimeOffset.MinValue, Guid.Empty);

            // Chặn token quá hạn (ví dụ quá 2 giờ)
            if (now - issuedAt > TimeSpan.FromHours(2))
                return (false, DateTimeOffset.MinValue, Guid.Empty);

            return (true, issuedAt, tierId);
        }
        catch
        {
            return (false, DateTimeOffset.MinValue, Guid.Empty);
        }
    }

    private static string PadBase64(string s) => (s.Length % 4) switch
    {
        2 => s + "==",
        3 => s + "=",
        _ => s
    };
}
