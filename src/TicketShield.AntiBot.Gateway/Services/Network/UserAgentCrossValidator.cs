using System.Text.RegularExpressions;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Dịch vụ đối soát chéo User-Agent giữa Client JavaScript và HTTP Header (US-13.2.4, SCRUM-377).
/// Áp dụng mô hình đối soát đa tầng (Exact -> Canonical WebViews -> Blatant Platform/Browser Mismatch -> Minor Skew).
/// Khử False Positive cho TicketShield Mobile App và các trình duyệt mạng xã hội (Zalo, Facebook, TikTok).
/// </summary>
public class UserAgentCrossValidator : IUserAgentCrossValidator
{
    // Regex bóc tách các hậu tố định danh của In-App WebViews
    private static readonly Regex TrailingTokenRegex = new(
        @"\s*(?:\[FBAN\/[^\]]+\]|\[FBAV\/[^\]]+\]|FB_IAB|FB4A|FBAV\/[^\s]+|Instagram\s+[^\s]+|Zalo\/[0-9.]+|TikTok\s+[^\s]+|Line\/[0-9.]+|Snapchat\s+[^\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public UserAgentCrossValidationResult Validate(string? clientUserAgent, string? headerUserAgent)
    {
        var cleanClient = clientUserAgent?.Trim();
        var cleanHeader = headerUserAgent?.Trim();

        // 1. MIỄN TRỪ AN TOÀN CHO MOBILE APP (TICKETSHIELD MOBILE APP)
        if (IsMobileApp(cleanHeader) || IsMobileApp(cleanClient))
        {
            return UserAgentCrossValidationResult.Consistent(
                UserAgentMatchLevel.NotApplicable,
                "TicketShield Mobile App request exempt from browser UA cross-validation");
        }

        // 2. XỬ LÝ TRƯỜNG HỢP CLIENT KHÔNG GỬI USER-AGENT
        if (string.IsNullOrWhiteSpace(cleanClient))
        {
            if (string.IsNullOrWhiteSpace(cleanHeader))
            {
                return UserAgentCrossValidationResult.Consistent(UserAgentMatchLevel.NotApplicable);
            }

            // Nếu Header là Desktop/Mobile browser thật nhưng Client JS hoàn toàn thiếu UA -> Dấu hiệu nghi vấn
            var headerPlat = DetectPlatform(cleanHeader);
            var headerBrowser = DetectBrowserFamily(cleanHeader);
            return UserAgentCrossValidationResult.Flagged(
                "Client telemetry missing User-Agent signal while HTTP Header claims browser access",
                clientPlat: null,
                headerPlat: headerPlat,
                clientBrowser: null,
                headerBrowser: headerBrowser);
        }

        if (string.IsNullOrWhiteSpace(cleanHeader))
        {
            return UserAgentCrossValidationResult.Blatant(
                "Missing HTTP User-Agent header while Client telemetry sent User-Agent",
                clientPlat: DetectPlatform(cleanClient),
                headerPlat: null,
                clientBrowser: DetectBrowserFamily(cleanClient),
                headerBrowser: null);
        }

        // 3. TẦNG 1: EXACT STRING MATCH (100% Khớp hoàn hảo)
        if (string.Equals(cleanClient, cleanHeader, StringComparison.Ordinal))
        {
            return UserAgentCrossValidationResult.Consistent(UserAgentMatchLevel.ExactMatch);
        }

        // 4. TẦNG 2: CANONICAL MATCH (Khử trailing tokens của In-App WebViews: Zalo, Facebook, TikTok)
        var normClient = TrailingTokenRegex.Replace(cleanClient, "").Trim();
        var normHeader = TrailingTokenRegex.Replace(cleanHeader, "").Trim();

        if (string.Equals(normClient, normHeader, StringComparison.OrdinalIgnoreCase))
        {
            return UserAgentCrossValidationResult.Consistent(
                UserAgentMatchLevel.CanonicalMatch,
                "User-Agent matched after normalizing In-App WebView trailing tokens");
        }

        // 5. TẦNG 3: BÓC TÁCH NỀN TẢNG (PLATFORM/OS) VÀ NHÂN TRÌNH DUYỆT (BROWSER FAMILY)
        var cPlat = DetectPlatform(cleanClient);
        var hPlat = DetectPlatform(cleanHeader);
        var cBrowser = DetectBrowserFamily(cleanClient);
        var hBrowser = DetectBrowserFamily(cleanHeader);

        // Check 5.1: Blatant Platform Mismatch (Lệch hệ điều hành nghiêm trọng)
        // Ví dụ: Client chạy trên Windows nhưng Header lại là Linux VPS hoặc iOS/Android
        if (cPlat != null && hPlat != null && !string.Equals(cPlat, hPlat, StringComparison.OrdinalIgnoreCase))
        {
            return UserAgentCrossValidationResult.Blatant(
                $"Blatant Platform Mismatch: Client JavaScript executed on {cPlat}, but HTTP Header claims {hPlat} (Bot Impersonation)",
                clientPlat: cPlat,
                headerPlat: hPlat,
                clientBrowser: cBrowser,
                headerBrowser: hBrowser);
        }

        // Check 5.2: Blatant Browser Engine Mismatch (Lệch nhân trình duyệt nghiêm trọng)
        // Ví dụ: Client là Chrome/Chromium nhưng Header lại là Firefox, cURL, Python, Go
        if (cBrowser != null && hBrowser != null && !string.Equals(cBrowser, hBrowser, StringComparison.OrdinalIgnoreCase))
        {
            return UserAgentCrossValidationResult.Blatant(
                $"Blatant Browser Mismatch: Client JavaScript is {cBrowser}, but HTTP Header claims {hBrowser} (Browser Spoofing)",
                clientPlat: cPlat,
                headerPlat: hPlat,
                clientBrowser: cBrowser,
                headerBrowser: hBrowser);
        }

        // Check 5.3: Headless Artifact Masking
        // Client lộ cờ HeadlessChrome nhưng Header HTTP lại xóa bỏ chữ Headless
        var cIsHeadless = cleanClient.Contains("HeadlessChrome", StringComparison.OrdinalIgnoreCase);
        var hIsHeadless = cleanHeader.Contains("HeadlessChrome", StringComparison.OrdinalIgnoreCase);
        if (cIsHeadless && !hIsHeadless)
        {
            return UserAgentCrossValidationResult.Blatant(
                "Headless Chromium artifact detected in Client JavaScript but masked in HTTP Header",
                clientPlat: cPlat,
                headerPlat: hPlat,
                clientBrowser: "HeadlessChrome",
                headerBrowser: hBrowser);
        }

        // 6. TẦNG 4: MINOR MISMATCH (Cùng OS, cùng Browser Family nhưng chuỗi khác nhau do phiên bản hoặc extension)
        return UserAgentCrossValidationResult.Flagged(
            "User-Agent strings differ between Client and Server, but share identical Platform and Browser Family",
            clientPlat: cPlat,
            headerPlat: hPlat,
            clientBrowser: cBrowser,
            headerBrowser: hBrowser);
    }

    private static bool IsMobileApp(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return false;
        return ua.Contains("TicketShield-Mobile-App", StringComparison.OrdinalIgnoreCase)
               || ua.Contains("TicketShieldMobile", StringComparison.OrdinalIgnoreCase)
               || ua.Contains("okhttp", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectPlatform(string ua)
    {
        if (ua.Contains("Windows NT", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Win64", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("WOW64", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Win32", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows";
        }

        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            return "Android";
        }

        if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("iPod", StringComparison.OrdinalIgnoreCase))
        {
            return "iOS";
        }

        if (ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase))
        {
            return "macOS";
        }

        if (ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("X11", StringComparison.OrdinalIgnoreCase))
        {
            return "Linux";
        }

        return null;
    }

    private static string? DetectBrowserFamily(string ua)
    {
        if (ua.Contains("HeadlessChrome", StringComparison.OrdinalIgnoreCase))
            return "HeadlessChrome";

        if (ua.Contains("curl/", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("python-requests", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("aiohttp", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Go-http-client", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("PostmanRuntime", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Wget", StringComparison.OrdinalIgnoreCase))
        {
            return "BotLibrary";
        }

        // Chromium-based (Chrome, Edge, Opera, Brave, Cốc Cốc)
        if (ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Chromium/", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("CriOS/", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("CocCoc", StringComparison.OrdinalIgnoreCase))
        {
            return "Chromium";
        }

        if (ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("FxiOS/", StringComparison.OrdinalIgnoreCase))
        {
            return "Firefox";
        }

        if (ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) &&
            !ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) &&
            !ua.Contains("Chromium/", StringComparison.OrdinalIgnoreCase))
        {
            return "Safari";
        }

        return null;
    }
}
