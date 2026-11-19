using System.Security.Cryptography;
using System.Text;
using TicketShield.AntiBot.Gateway.Models;

namespace TicketShield.AntiBot.Gateway.Services;

public interface ISessionTokenService
{
    /// <summary>
    /// Phát hành SessionToken v1 cũ (tương thích ngược).
    /// </summary>
    string GenerateSessionToken(Guid tierId, DateTimeOffset? issuedAt = null);

    /// <summary>
    /// Phát hành SessionToken v2 có nhúng One-Time Cryptographic Nonce và TTL 300s (US-13.2.3, SCRUM-376).
    /// </summary>
    (string Token, string Nonce, DateTimeOffset IssuedAt, int ExpiresInSeconds) GenerateSessionTokenWithNonce(
        Guid tierId,
        DateTimeOffset? issuedAt = null,
        int expiresInSeconds = 300);

    /// <summary>
    /// Xác thực token và trả về tuple 3 phần tử (tương thích ngược 100%).
    /// </summary>
    (bool IsValid, DateTimeOffset IssuedAt, Guid TierId) ValidateSessionToken(string? token);

    /// <summary>
    /// Xác thực token v2 và trích xuất Nonce cùng version chi tiết (US-13.2.3).
    /// </summary>
    SessionTokenValidationResult ValidateSessionTokenV2(string? token);
}

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
        var now = issuedAt ?? _clock.GetUtcNow();
        var epochMs = now.ToUnixTimeMilliseconds();
        var sessionId = Guid.NewGuid();

        // Sinh One-Time Cryptographic Nonce an toàn (16 bytes = 32 hex chars)
        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonce = Convert.ToHexString(nonceBytes).ToLowerInvariant();

        // Payload v2 nhúng Nonce: {sessionId}:{tierId}:{epochMs}:{nonce}
        var payload = $"{sessionId:N}:{tierId:D}:{epochMs}:{nonce}";
        var token = SignPayload(payload);

        return (token, nonce, now, expiresInSeconds);
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
            return SessionTokenValidationResult.Failed("Token format is invalid (expected 2 parts).");

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

            // Hỗ trợ cả v1 (3 segments) và v2 (4 segments kèm Nonce)
            if (segments.Length != 3 && segments.Length != 4)
                return SessionTokenValidationResult.Failed($"Invalid payload segments count: {segments.Length}");

            if (!Guid.TryParse(segments[0], out _) ||
                !Guid.TryParse(segments[1], out var tierId) ||
                !long.TryParse(segments[2], out var epochMs))
            {
                return SessionTokenValidationResult.Failed("Failed to parse token payload values.");
            }

            string? nonce = null;
            var tokenVersion = 1;
            if (segments.Length == 4)
            {
                nonce = segments[3];
                tokenVersion = 2;
            }

            var issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
            var now = _clock.GetUtcNow();

            // Dung sai lệch đồng hồ client-server (Clock skew tolerance 15 giây)
            if (issuedAt > now.AddSeconds(15))
                return SessionTokenValidationResult.Failed("Token issued in the future (clock skew exceeded 15s).");

            // Kiểm tra TTL: Token v2 áp dụng TTL 300 giây (5 phút), Token v1 cũ áp dụng 2 giờ
            var maxTtl = tokenVersion == 2 ? TimeSpan.FromSeconds(300) : TimeSpan.FromHours(2);
            if (now - issuedAt > maxTtl)
                return SessionTokenValidationResult.Failed($"Token expired (exceeded max TTL of {maxTtl.TotalSeconds}s).");

            return SessionTokenValidationResult.Success(issuedAt, tierId, nonce, tokenVersion);
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
