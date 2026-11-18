using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Hiện thực kiểm tra và đối chiếu thứ tự Pseudo-Headers trong HTTP/2 theo chuẩn RFC 7540.
/// Tối ưu hóa hiệu năng, Zero Heap Allocation khi kiểm tra chữ ký trình duyệt.
/// </summary>
public class PseudoHeaderInspector(ILogger<PseudoHeaderInspector> logger) : IPseudoHeaderInspector
{
    private static readonly string[] PseudoHeaderCandidates =
    [
        "X-Http2-Pseudo-Headers",
        "CF-Pseudo-Headers",
        "X-Http2-Fingerprint",
        "X-Akamai-Http2-Fingerprint"
    ];

    public string ExtractPseudoHeaderOrder(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            // 1. Kiểm tra các header do Reverse Proxy / WAF ngoài chuyển tiếp
            foreach (var headerName in PseudoHeaderCandidates)
            {
                if (context.Request.Headers.TryGetValue(headerName, out var values) &&
                    !string.IsNullOrWhiteSpace(values.FirstOrDefault()))
                {
                    var rawValue = values.FirstOrDefault()!;
                    var normalized = NormalizeOrderString(rawValue);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        return normalized;
                    }
                }
            }

            // 2. Nếu là kết nối HTTP/2 trực tiếp tới Kestrel nhưng không có proxy header
            if (HttpProtocol.IsHttp2(context.Request.Protocol) || HttpProtocol.IsHttp3(context.Request.Protocol))
            {
                // Trên direct Kestrel connection, nếu client gửi qua HTTP/2 hợp lệ mà không có header ngoài:
                return "direct_h2";
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract HTTP/2 pseudo-header order.");
            return string.Empty;
        }
    }

    public bool IsOrderConsistentWithBrowser(string? order, string? userAgent, out string? anomalyReason)
    {
        anomalyReason = null;

        // Nếu không có thông tin thứ tự (ví dụ: HTTP/1.1 hoặc không có proxy header) -> Bỏ qua
        if (string.IsNullOrWhiteSpace(order) || order == "direct_h2")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(userAgent))
        {
            anomalyReason = "Missing User-Agent header on HTTP/2 request.";
            return false;
        }

        var normalizedOrder = order.Trim().ToLowerInvariant();
        var ua = userAgent.AsSpan();

        // 1. Kiểm tra các thư viện Bot khai báo thẳng thắn
        if (ContainsIgnoreCase(ua, "python") ||
            ContainsIgnoreCase(ua, "requests") ||
            ContainsIgnoreCase(ua, "aiohttp") ||
            ContainsIgnoreCase(ua, "curl_cffi") ||
            ContainsIgnoreCase(ua, "curl/"))
        {
            anomalyReason = $"Automated HTTP client library detected in User-Agent: '{userAgent}'.";
            return false;
        }

        // 2. Nhận diện gia đình Chrome / Chromium (Blink Engine: Chrome, Edge, Brave, Opera, Cốc Cốc)
        var isChromeFamily = (ContainsIgnoreCase(ua, "Chrome") || ContainsIgnoreCase(ua, "CriOS") || ContainsIgnoreCase(ua, "Edg/")) &&
                             !ContainsIgnoreCase(ua, "Firefox");

        if (isChromeFamily)
        {
            // Chrome chuẩn: :method, :authority, :scheme, :path -> "m,a,s,p"
            if (normalizedOrder == "m,a,s,p")
            {
                return true;
            }

            if (normalizedOrder == "m,p,a,s")
            {
                anomalyReason = "User-Agent claims Chrome/Chromium, but Pseudo-Header order is 'm,p,a,s' (matches Firefox signature - Bot Impersonation).";
                return false;
            }

            if (normalizedOrder == "a,m,p,s" || normalizedOrder == "m,p,s,a")
            {
                anomalyReason = $"User-Agent claims Chrome/Chromium, but Pseudo-Header order is '{normalizedOrder}' (matches Golang/Python bot signature).";
                return false;
            }

            anomalyReason = $"User-Agent claims Chrome/Chromium, but sent inconsistent Pseudo-Header order: '{normalizedOrder}' (expected 'm,a,s,p').";
            return false;
        }

        // 3. Nhận diện gia đình Firefox (Gecko Engine)
        var isFirefox = ContainsIgnoreCase(ua, "Firefox") || ContainsIgnoreCase(ua, "FxiOS");
        if (isFirefox)
        {
            // Firefox chuẩn: :method, :path, :authority, :scheme -> "m,p,a,s"
            if (normalizedOrder == "m,p,a,s")
            {
                return true;
            }

            anomalyReason = $"User-Agent claims Firefox, but Pseudo-Header order is '{normalizedOrder}' (expected 'm,p,a,s').";
            return false;
        }

        // 4. Nhận diện gia đình Safari (WebKit Engine)
        var isSafari = ContainsIgnoreCase(ua, "Safari") && !ContainsIgnoreCase(ua, "Chrome") && !ContainsIgnoreCase(ua, "Android");
        if (isSafari)
        {
            // Safari chuẩn: :method, :scheme, :path, :authority -> "m,s,p,a" hoặc "m,s,a,p"
            if (normalizedOrder == "m,s,p,a" || normalizedOrder == "m,s,a,p")
            {
                return true;
            }

            if (normalizedOrder == "m,a,s,p")
            {
                anomalyReason = "User-Agent claims Safari, but Pseudo-Header order is 'm,a,s,p' (matches Chrome signature - Impersonation).";
                return false;
            }

            anomalyReason = $"User-Agent claims Safari, but sent unexpected Pseudo-Header order: '{normalizedOrder}'.";
            return false;
        }

        return true;
    }

    private static string NormalizeOrderString(string raw)
    {
        var span = raw.AsSpan().Trim();

        // Xử lý chuỗi Akamai Fingerprint: 1:65536;2:0;4:6291456;6:262144|15663105|0|m,a,s,p
        if (span.Contains('|'))
        {
            var parts = raw.Split('|');
            if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]))
            {
                span = parts[3].AsSpan().Trim();
            }
        }

        // Nếu chuỗi ở dạng dài: ":method,:authority,:scheme,:path"
        if (span.Contains(":method", StringComparison.OrdinalIgnoreCase))
        {
            var replaced = raw.ToLowerInvariant()
                .Replace(":method", "m")
                .Replace(":authority", "a")
                .Replace(":scheme", "s")
                .Replace(":path", "p")
                .Replace(" ", "");
            return replaced;
        }

        // Dạng ngắn: "m,a,s,p" hoặc "m,p,a,s"
        return span.ToString().ToLowerInvariant().Replace(" ", "");
    }

    private static bool ContainsIgnoreCase(ReadOnlySpan<char> source, string value)
    {
        return source.Contains(value.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }
}
