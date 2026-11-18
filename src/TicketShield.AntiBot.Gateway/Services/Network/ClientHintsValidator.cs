using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Dịch vụ đối soát tính nhất quán giữa User-Agent và W3C Client Hints (Sec-Ch-Ua, Sec-Fetch-*) (NET-13.1.3).
/// Khử triệt để false positive cho Safari, Firefox, và Mobile App.
/// Bắt quả tang bot giả mạo trình duyệt (curl_cffi, python-requests, Puppeteer không hoàn chỉnh).
/// </summary>
public class ClientHintsValidator : IClientHintsValidator
{
    public ClientHintsProfile Validate(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Validate(context.Request.Headers);
    }

    public ClientHintsProfile Validate(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var userAgent = headers.UserAgent.ToString().Trim();
        var rawSecChUa = headers["Sec-Ch-Ua"].ToString().Trim();
        var secChUaMobile = headers["Sec-Ch-Ua-Mobile"].ToString().Trim();
        var secChUaPlatform = headers["Sec-Ch-Ua-Platform"].ToString().Trim();
        var secFetchSite = headers["Sec-Fetch-Site"].ToString().Trim();
        var secFetchMode = headers["Sec-Fetch-Mode"].ToString().Trim();
        var secFetchDest = headers["Sec-Fetch-Dest"].ToString().Trim();

        // 1. Kiểm tra User-Agent cơ bản
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return new ClientHintsProfile
            {
                Status = ClientHintsStatus.MissingHints,
                InconsistencyScore = 1.0f,
                AnomalyReason = "Missing or empty User-Agent header"
            };
        }

        var isChromium = IsChromiumUserAgent(userAgent);
        var isSafari = IsSafariUserAgent(userAgent);
        var isFirefox = IsFirefoxUserAgent(userAgent);
        var isMobileApp = IsMobileAppUserAgent(userAgent);

        // 2. Xử lý các client không hỗ trợ Client Hints theo chuẩn W3C (Safari, Firefox, Mobile App)
        if (!isChromium && (isSafari || isFirefox || isMobileApp))
        {
            // Nếu Safari/Firefox/App Mobile thật nhưng bot lại gửi kèm header Sec-Ch-Ua của Chromium -> Giả mạo bất thường!
            if (!string.IsNullOrEmpty(rawSecChUa) && rawSecChUa.Contains("Chromium", StringComparison.OrdinalIgnoreCase))
            {
                return new ClientHintsProfile
                {
                    Status = ClientHintsStatus.BrandMismatch,
                    RawSecChUa = rawSecChUa,
                    SecChUaMobile = secChUaMobile,
                    SecChUaPlatform = secChUaPlatform,
                    SecFetchSite = secFetchSite,
                    SecFetchMode = secFetchMode,
                    SecFetchDest = secFetchDest,
                    InconsistencyScore = 1.0f,
                    AnomalyReason = $"Non-Chromium client ({userAgent}) unexpectedly sent Chromium Client Hints"
                };
            }

            // Safari/Firefox/Mobile App hợp lệ (không bắt buộc Client Hints) -> Score = 0
            return new ClientHintsProfile
            {
                Status = ClientHintsStatus.NotApplicable,
                RawSecChUa = string.IsNullOrEmpty(rawSecChUa) ? null : rawSecChUa,
                SecChUaMobile = string.IsNullOrEmpty(secChUaMobile) ? null : secChUaMobile,
                SecChUaPlatform = string.IsNullOrEmpty(secChUaPlatform) ? null : secChUaPlatform,
                SecFetchSite = string.IsNullOrEmpty(secFetchSite) ? null : secFetchSite,
                SecFetchMode = string.IsNullOrEmpty(secFetchMode) ? null : secFetchMode,
                SecFetchDest = string.IsNullOrEmpty(secFetchDest) ? null : secFetchDest,
                InconsistencyScore = 0.0f,
                AnomalyReason = null
            };
        }

        // 3. Nếu không phải Chromium và cũng không phải Safari/Firefox (ví dụ: cURL thuần, Postman, custom script)
        if (!isChromium)
        {
            // Nếu không có Sec-Ch-Ua thì giữ NotApplicable cho phép các tool API hợp lệ
            if (string.IsNullOrEmpty(rawSecChUa))
            {
                return new ClientHintsProfile
                {
                    Status = ClientHintsStatus.NotApplicable,
                    InconsistencyScore = 0.0f
                };
            }
        }

        // 4. BẮT ĐẦU ĐỐI SOÁT CHROMIUM-BASED CLIENTS (Chrome, Edge, Brave, Opera, Cốc Cốc)
        // Check 4.1: User-Agent là Chromium nhưng thiếu hoàn toàn Sec-Ch-Ua (Dấu hiệu đặc trưng của curl_cffi / requests / aiohttp)
        if (string.IsNullOrWhiteSpace(rawSecChUa))
        {
            return new ClientHintsProfile
            {
                Status = ClientHintsStatus.MissingHints,
                InconsistencyScore = 1.0f,
                AnomalyReason = $"User-Agent claims Chromium ({ExtractBrandSummary(userAgent)}) but Sec-Ch-Ua header is missing entirely (bot library impersonation)"
            };
        }

        // Parse danh sách brands và versions từ Sec-Ch-Ua
        var parsedBrands = ParseSecChUaBrands(rawSecChUa);
        var brandNames = parsedBrands.Select(b => $"{b.Brand}:{b.Version}").ToList();

        // Check 4.2: Đối soát Brand tương ứng với User-Agent
        var isEdge = userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase);
        if (isEdge)
        {
            var hasEdgeBrand = parsedBrands.Any(b =>
                b.Brand.Contains("Microsoft Edge", StringComparison.OrdinalIgnoreCase) ||
                b.Brand.Contains("Edge", StringComparison.OrdinalIgnoreCase));

            if (!hasEdgeBrand)
            {
                return new ClientHintsProfile
                {
                    Status = ClientHintsStatus.BrandMismatch,
                    RawSecChUa = rawSecChUa,
                    ExtractedBrands = brandNames,
                    SecChUaMobile = secChUaMobile,
                    SecChUaPlatform = secChUaPlatform,
                    InconsistencyScore = 1.0f,
                    AnomalyReason = "User-Agent claims Microsoft Edge but Sec-Ch-Ua does not contain Edge brand"
                };
            }
        }
        else
        {
            // Chrome hoặc Chromium browser
            var hasChromeBrand = parsedBrands.Any(b =>
                b.Brand.Contains("Google Chrome", StringComparison.OrdinalIgnoreCase) ||
                b.Brand.Contains("Chromium", StringComparison.OrdinalIgnoreCase));

            if (!hasChromeBrand)
            {
                return new ClientHintsProfile
                {
                    Status = ClientHintsStatus.BrandMismatch,
                    RawSecChUa = rawSecChUa,
                    ExtractedBrands = brandNames,
                    SecChUaMobile = secChUaMobile,
                    SecChUaPlatform = secChUaPlatform,
                    InconsistencyScore = 1.0f,
                    AnomalyReason = "User-Agent claims Chrome/Chromium but Sec-Ch-Ua lacks Google Chrome/Chromium brands"
                };
            }
        }

        // Check 4.3: Đối soát Major Version giữa User-Agent và Sec-Ch-Ua
        var uaMajorVersion = ExtractMajorVersionFromUa(userAgent, isEdge ? "Edg/" : "Chrome/");
        if (!string.IsNullOrEmpty(uaMajorVersion))
        {
            // Kiểm tra xem có brand tương ứng nào có version khớp với major version trong UA không
            var matchingVersion = parsedBrands.Any(b =>
                (isEdge
                    ? (b.Brand.Contains("Edge", StringComparison.OrdinalIgnoreCase) || b.Brand.Contains("Chromium", StringComparison.OrdinalIgnoreCase))
                    : (b.Brand.Contains("Chrome", StringComparison.OrdinalIgnoreCase) || b.Brand.Contains("Chromium", StringComparison.OrdinalIgnoreCase))) &&
                b.Version.Equals(uaMajorVersion, StringComparison.OrdinalIgnoreCase));

            if (!matchingVersion)
            {
                return new ClientHintsProfile
                {
                    Status = ClientHintsStatus.VersionMismatch,
                    RawSecChUa = rawSecChUa,
                    ExtractedBrands = brandNames,
                    SecChUaMobile = secChUaMobile,
                    SecChUaPlatform = secChUaPlatform,
                    InconsistencyScore = 1.0f,
                    AnomalyReason = $"Version skew detected: User-Agent indicates major version {uaMajorVersion}, but Sec-Ch-Ua versions are [{string.Join(", ", parsedBrands.Select(b => $"{b.Brand} v={b.Version}"))}]"
                };
            }
        }

        // Check 4.4: Đối soát Platform (Sec-Ch-Ua-Platform)
        if (!string.IsNullOrEmpty(secChUaPlatform))
        {
            var cleanPlatform = secChUaPlatform.Trim().Trim('\"');
            var platformMismatch = CheckPlatformMismatch(userAgent, cleanPlatform);
            if (platformMismatch != null)
            {
                return new ClientHintsProfile
                {
                    Status = ClientHintsStatus.PlatformMismatch,
                    RawSecChUa = rawSecChUa,
                    ExtractedBrands = brandNames,
                    SecChUaMobile = secChUaMobile,
                    SecChUaPlatform = secChUaPlatform,
                    InconsistencyScore = 1.0f,
                    AnomalyReason = platformMismatch
                };
            }
        }

        // Check 4.5: Đối soát Mobile Flag (Sec-Ch-Ua-Mobile)
        if (!string.IsNullOrEmpty(secChUaMobile))
        {
            var cleanMobile = secChUaMobile.Trim().Trim('\"');
            var mobileMismatch = CheckMobileFlagMismatch(userAgent, cleanMobile);
            if (mobileMismatch != null)
            {
                return new ClientHintsProfile
                {
                    Status = ClientHintsStatus.MobileFlagMismatch,
                    RawSecChUa = rawSecChUa,
                    ExtractedBrands = brandNames,
                    SecChUaMobile = secChUaMobile,
                    SecChUaPlatform = secChUaPlatform,
                    InconsistencyScore = 1.0f,
                    AnomalyReason = mobileMismatch
                };
            }
        }

        // Check 4.6: Đối soát Sec-Fetch-* Headers
        // Một trình duyệt Chromium hiện đại (Chrome 120+) khi gửi request API/Form luôn kèm Sec-Fetch-*
        if (string.IsNullOrEmpty(secFetchSite) && string.IsNullOrEmpty(secFetchMode) && string.IsNullOrEmpty(secFetchDest))
        {
            // Thiếu hoàn toàn cả 3 header Sec-Fetch trên trình duyệt Chromium hiện đại
            return new ClientHintsProfile
            {
                Status = ClientHintsStatus.SecFetchMismatch,
                RawSecChUa = rawSecChUa,
                ExtractedBrands = brandNames,
                SecChUaMobile = secChUaMobile,
                SecChUaPlatform = secChUaPlatform,
                InconsistencyScore = 1.0f,
                AnomalyReason = "Missing all Sec-Fetch-* headers on a modern Chromium User-Agent"
            };
        }

        // Khớp hoàn toàn (Consistent) -> Điểm bất thường = 0
        return new ClientHintsProfile
        {
            Status = ClientHintsStatus.Consistent,
            RawSecChUa = rawSecChUa,
            ExtractedBrands = brandNames,
            SecChUaMobile = secChUaMobile,
            SecChUaPlatform = secChUaPlatform,
            SecFetchSite = secFetchSite,
            SecFetchMode = secFetchMode,
            SecFetchDest = secFetchDest,
            InconsistencyScore = 0.0f,
            AnomalyReason = null
        };
    }

    #region Helper Methods

    private static bool IsChromiumUserAgent(string ua) =>
        (ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ||
         ua.Contains("Chromium/", StringComparison.OrdinalIgnoreCase) ||
         ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase));

    private static bool IsSafariUserAgent(string ua) =>
        ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) &&
        !ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) &&
        !ua.Contains("Chromium/", StringComparison.OrdinalIgnoreCase) &&
        !ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase);

    private static bool IsFirefoxUserAgent(string ua) =>
        (ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ||
         ua.Contains("Gecko/", StringComparison.OrdinalIgnoreCase)) &&
        !ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase);

    private static bool IsMobileAppUserAgent(string ua) =>
        ua.Contains("TicketShield", StringComparison.OrdinalIgnoreCase) ||
        ua.Contains("okhttp", StringComparison.OrdinalIgnoreCase) ||
        ua.Contains("Dart", StringComparison.OrdinalIgnoreCase) ||
        ua.Contains("CFNetwork", StringComparison.OrdinalIgnoreCase);

    private static string ExtractBrandSummary(string ua)
    {
        if (ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        return "Chromium";
    }

    /// <summary>
    /// Bóc tách cấu trúc: `"Chromium";v="130", "Google Chrome";v="130", "Not?A_Brand";v="99"`
    /// </summary>
    public static List<(string Brand, string Version)> ParseSecChUaBrands(string raw)
    {
        var result = new List<(string Brand, string Version)>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        var entries = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) continue;

            var brand = parts[0].Trim('\"');
            var version = string.Empty;

            for (int i = 1; i < parts.Length; i++)
            {
                var param = parts[i].Trim();
                if (param.StartsWith("v=", StringComparison.OrdinalIgnoreCase))
                {
                    version = param[2..].Trim().Trim('\"');
                    break;
                }
            }

            result.Add((brand, version));
        }

        return result;
    }

    private static string? ExtractMajorVersionFromUa(string ua, string token)
    {
        var idx = ua.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var start = idx + token.Length;
        var end = start;
        while (end < ua.Length && char.IsDigit(ua[end]))
        {
            end++;
        }

        return end > start ? ua[start..end] : null;
    }

    private static string? CheckPlatformMismatch(string ua, string cleanPlatform)
    {
        var isWindowsUa = ua.Contains("Windows NT", StringComparison.OrdinalIgnoreCase);
        var isMacUa = ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) || ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase);
        var isAndroidUa = ua.Contains("Android", StringComparison.OrdinalIgnoreCase);
        var isLinuxUa = ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) && !isAndroidUa;

        if (isWindowsUa && !cleanPlatform.Equals("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return $"Platform mismatch: User-Agent indicates Windows, but Sec-Ch-Ua-Platform is \"{cleanPlatform}\"";
        }

        if (isMacUa && !cleanPlatform.Equals("macOS", StringComparison.OrdinalIgnoreCase) && !cleanPlatform.Equals("Mac OS X", StringComparison.OrdinalIgnoreCase))
        {
            return $"Platform mismatch: User-Agent indicates macOS, but Sec-Ch-Ua-Platform is \"{cleanPlatform}\"";
        }

        if (isAndroidUa && !cleanPlatform.Equals("Android", StringComparison.OrdinalIgnoreCase))
        {
            return $"Platform mismatch: User-Agent indicates Android, but Sec-Ch-Ua-Platform is \"{cleanPlatform}\"";
        }

        if (isLinuxUa && !cleanPlatform.Equals("Linux", StringComparison.OrdinalIgnoreCase))
        {
            return $"Platform mismatch: User-Agent indicates Linux, but Sec-Ch-Ua-Platform is \"{cleanPlatform}\"";
        }

        return null;
    }

    private static string? CheckMobileFlagMismatch(string ua, string cleanMobile)
    {
        var isMobileUa = ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase) || ua.Contains("Android", StringComparison.OrdinalIgnoreCase);

        // ?1 là Mobile, ?0 là Desktop
        if (cleanMobile == "?1" && !isMobileUa)
        {
            return "Mobile flag mismatch: Sec-Ch-Ua-Mobile is ?1 (Mobile) but User-Agent indicates a Desktop browser";
        }

        if (cleanMobile == "?0" && isMobileUa && ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase))
        {
            return "Mobile flag mismatch: Sec-Ch-Ua-Mobile is ?0 (Desktop) but User-Agent indicates a Mobile device";
        }

        return null;
    }

    #endregion
}
