using System.Globalization;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Hiện thực bộ phân tích cú pháp JA4 Fingerprint hiệu năng cao.
/// Tối ưu hóa bằng ReadOnlySpan&lt;char&gt; để không cấp phát bộ nhớ (Zero allocation khi validate).
/// Đạt chuẩn đặc tả kỹ thuật JA4+ của Fox-IT / John Althouse:
/// Format: {JA4_a}_{JA4_b}_{JA4_c} (Tổng cộng 36 ký tự)
/// Ví dụ: t13d1516h2_8daaf6152771_02711d0e519e
/// </summary>
public class Ja4Parser : IJa4Parser
{
    private const int ExpectedJa4Length = 36;
    private const int IndexUnderscore1 = 10;
    private const int IndexUnderscore2 = 23;

    public bool TryParse(string? rawJa4, out ParsedJa4 parsed)
    {
        parsed = ParsedJa4.Empty;

        if (string.IsNullOrWhiteSpace(rawJa4))
        {
            return false;
        }

        var span = rawJa4.AsSpan().Trim();
        if (span.Length != ExpectedJa4Length)
        {
            return false;
        }

        // 1. Kiểm tra 2 dấu gạch dưới ngăn cách 3 thành phần
        if (span[IndexUnderscore1] != '_' || span[IndexUnderscore2] != '_')
        {
            return false;
        }

        // 2. Phân tích JA4_a (10 ký tự: 0..9)
        // Ký tự 0: Protocol ('t' = TCP/TLS, 'q' = QUIC, 'd' = DTLS)
        var protocol = char.ToLowerInvariant(span[0]);
        if (protocol != 't' && protocol != 'q' && protocol != 'd')
        {
            return false;
        }

        // Ký tự 1-2: TLS Version ("13", "12", "11", "10", "s3", "s2", "00")
        var tlsVerSpan = span.Slice(1, 2);
        if (!char.IsAsciiLetterOrDigit(tlsVerSpan[0]) || !char.IsAsciiLetterOrDigit(tlsVerSpan[1]))
        {
            return false;
        }

        // Ký tự 3: SNI indicator ('d' = domain, 'i' = ip, '0' = none)
        var sniType = char.ToLowerInvariant(span[3]);
        if (sniType != 'd' && sniType != 'i' && sniType != '0')
        {
            return false;
        }

        // Ký tự 4-5: Số lượng Cipher suites (2 chữ số 00-99)
        var cipherCountSpan = span.Slice(4, 2);
        if (!char.IsAsciiDigit(cipherCountSpan[0]) || !char.IsAsciiDigit(cipherCountSpan[1]) ||
            !int.TryParse(cipherCountSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var cipherCount))
        {
            return false;
        }

        // Ký tự 6-7: Số lượng Extensions (2 chữ số 00-99)
        var extCountSpan = span.Slice(6, 2);
        if (!char.IsAsciiDigit(extCountSpan[0]) || !char.IsAsciiDigit(extCountSpan[1]) ||
            !int.TryParse(extCountSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var extensionCount))
        {
            return false;
        }

        // Ký tự 8-9: ALPN đàm phán ("h2", "h1", "00", ...)
        var alpnSpan = span.Slice(8, 2);
        if (!char.IsAsciiLetterOrDigit(alpnSpan[0]) || !char.IsAsciiLetterOrDigit(alpnSpan[1]))
        {
            return false;
        }

        // 3. Phân tích JA4_b (12 ký tự hex: 11..22) - Hash ciphers
        var cipherHashSpan = span.Slice(11, 12);
        if (!IsAllAsciiHex(cipherHashSpan))
        {
            return false;
        }

        // 4. Phân tích JA4_c (12 ký tự hex: 24..35) - Hash extensions
        var extensionHashSpan = span.Slice(24, 12);
        if (!IsAllAsciiHex(extensionHashSpan))
        {
            return false;
        }

        // Tất cả điều kiện cấu trúc JA4 đều thỏa mãn
        parsed = new ParsedJa4
        {
            RawJa4 = span.ToString(),
            Protocol = protocol,
            TlsVersion = tlsVerSpan.ToString().ToLowerInvariant(),
            SniType = sniType,
            CipherCount = cipherCount,
            ExtensionCount = extensionCount,
            Alpn = alpnSpan.ToString().ToLowerInvariant(),
            CipherHash = cipherHashSpan.ToString().ToLowerInvariant(),
            ExtensionHash = extensionHashSpan.ToString().ToLowerInvariant(),
            IsValid = true
        };

        return true;
    }

    public ParsedJa4 ParseOrDefault(string? rawJa4)
    {
        return TryParse(rawJa4, out var parsed) ? parsed : ParsedJa4.Empty;
    }

    private static bool IsAllAsciiHex(ReadOnlySpan<char> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            if (!char.IsAsciiHexDigit(span[i]))
            {
                return false;
            }
        }
        return true;
    }
}
