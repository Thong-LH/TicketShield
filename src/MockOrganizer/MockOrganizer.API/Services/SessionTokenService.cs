using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using MockOrganizer.API.Models;

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
        return SignPayload(payload);
    }

    public (string Token, string Nonce, DateTimeOffset IssuedAt, int ExpiresInSeconds) GenerateSessionTokenWithNonce(
        Guid tierId,
        DateTimeOffset? issuedAt = null,
        int expiresInSeconds = 300)
    {
        var (token, nonce, issued, _, expires) = GenerateSessionTokenV3(tierId, null, issuedAt, 0, expiresInSeconds);
        return (token, nonce, issued, expires);
    }

    public (string Token, string Nonce, DateTimeOffset IssuedAt, DateTimeOffset SessionStart, int ExpiresInSeconds) GenerateSessionTokenV3(
        Guid tierId,
        DateTimeOffset? sessionStart = null,
        DateTimeOffset? issuedAt = null,
        int refreshCount = 0,
        int expiresInSeconds = 300)
    {
        var now = issuedAt ?? _clock.GetUtcNow();
        var start = sessionStart ?? now;
        var epochMs = now.ToUnixTimeMilliseconds();
        var sessionStartMs = start.ToUnixTimeMilliseconds();
        var sessionId = Guid.NewGuid();

        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonce = Convert.ToHexString(nonceBytes).ToLowerInvariant();

        // Token v3 (6 segments): sessionId:tierId:issuedAtMs:nonce:sessionStartMs:refreshCount
        var payload = $"{sessionId:N}:{tierId:D}:{epochMs}:{nonce}:{sessionStartMs}:{refreshCount}";
        var token = SignPayload(payload);

        return (token, nonce, now, start, expiresInSeconds);
    }

    private string SignPayload(string payload)
    {
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
        var result = ValidateSessionTokenV2(token);
        return (result.IsValid, result.IssuedAt, result.TierId);
    }

    public SessionTokenValidationResult ValidateSessionTokenV2(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return SessionTokenValidationResult.Failed("Token is null or empty.");

        var parts = token.Split('.');
        if (parts.Length != 2)
            return SessionTokenValidationResult.Failed("Token format is invalid.");

        try
        {
            var payloadBase64 = PadBase64(parts[0].Replace('-', '+').Replace('_', '/'));
            var payloadBytes = Convert.FromBase64String(payloadBase64);

            using var hmac = new HMACSHA256(_keyBytes);
            var expectedSignature = hmac.ComputeHash(payloadBytes);

            var actualSignatureBase64 = PadBase64(parts[1].Replace('-', '+').Replace('_', '/'));
            var actualSignature = Convert.FromBase64String(actualSignatureBase64);

            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
                return SessionTokenValidationResult.Failed("HMAC signature mismatch.");

            var payload = Encoding.UTF8.GetString(payloadBytes);
            var segments = payload.Split(':');

            // Hỗ trợ v1 (3 segments), v2 (4 segments kèm Nonce), v3 (6 segments kèm SessionStart & RefreshCount)
            if (segments.Length != 3 && segments.Length != 4 && segments.Length != 6)
                return SessionTokenValidationResult.Failed($"Invalid payload segments count: {segments.Length}");

            if (!Guid.TryParse(segments[0], out _) ||
                !Guid.TryParse(segments[1], out var tierId) ||
                !long.TryParse(segments[2], out var epochMs))
            {
                return SessionTokenValidationResult.Failed("Failed to parse token payload values.");
            }

            string? nonce = null;
            var tokenVersion = 1;
            var issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
            var sessionStart = issuedAt;
            var refreshCount = 0;

            if (segments.Length >= 4)
            {
                nonce = segments[3];
                tokenVersion = 2;
            }

            if (segments.Length == 6)
            {
                if (long.TryParse(segments[4], out var startMs))
                {
                    sessionStart = DateTimeOffset.FromUnixTimeMilliseconds(startMs);
                }
                if (int.TryParse(segments[5], out var rCount))
                {
                    refreshCount = rCount;
                }
                tokenVersion = 3;
            }

            var now = _clock.GetUtcNow();

            // Dung sai lệch đồng hồ client-server (Clock skew tolerance 5 giây)
            if (issuedAt > now.AddSeconds(5))
                return SessionTokenValidationResult.Failed("Token issued in the future (clock skew exceeded 5s).");

            var maxTtl = tokenVersion >= 2 ? TimeSpan.FromSeconds(300) : TimeSpan.FromHours(2);
            if (now - issuedAt > maxTtl)
                return SessionTokenValidationResult.Failed($"Token expired (exceeded max TTL of {maxTtl.TotalSeconds}s).");

            // Ràng buộc an ninh v3: Trần tuổi phiên tối đa 15 phút (900s)
            if (tokenVersion == 3 && now - sessionStart > TimeSpan.FromMinutes(15))
                return SessionTokenValidationResult.Failed("Session lifetime exceeded maximum ceiling of 15 minutes.");

            // Ràng buộc an ninh v3: Trần số lần refresh tối đa 3 lần
            if (tokenVersion == 3 && refreshCount > 3)
                return SessionTokenValidationResult.Failed("Session exceeded maximum refresh limit of 3.");

            return SessionTokenValidationResult.Success(issuedAt, tierId, nonce, tokenVersion, sessionStart, refreshCount);
        }
        catch (Exception ex)
        {
            return SessionTokenValidationResult.Failed($"Validation exception: {ex.Message}");
        }
    }

    private static string PadBase64(string s) => (s.Length % 4) switch
    {
        2 => s + "==",
        3 => s + "=",
        _ => s
    };
}
