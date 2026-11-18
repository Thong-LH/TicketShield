using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Hiện thực bóc tách và kiểm tra tham số HTTP/2 SETTINGS Frame theo chuẩn RFC 7540 & Akamai.
/// Tối ưu hóa hiệu năng bằng ReadOnlySpan&lt;char&gt;, không ném ngoại lệ (Exception-safe).
/// </summary>
public class Http2SettingsInspector(ILogger<Http2SettingsInspector> logger) : IHttp2SettingsInspector
{
    private const uint ChromeExpectedInitialWindowSize = 6291456; // 6MB (Blink Engine signature)
    private const uint FirefoxInitialWindowSize = 131072;        // 128KB (Gecko Engine signature)
    private const uint RfcDefaultInitialWindowSize = 65535;      // RFC 7540 Default (Python/cURL/Hyper bots)

    private static readonly string[] SettingsHeaderCandidates =
    [
        "X-Http2-Settings",
        "CF-Http2-Settings",
        "X-Http2-Fingerprint",
        "X-Akamai-Http2-Fingerprint"
    ];

    public Http2SettingsFrame ExtractSettings(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            foreach (var headerName in SettingsHeaderCandidates)
            {
                if (context.Request.Headers.TryGetValue(headerName, out var values) &&
                    !string.IsNullOrWhiteSpace(values.FirstOrDefault()))
                {
                    var rawValue = values.FirstOrDefault()!;
                    if (TryParseSettingsString(rawValue, out var parsedSettings))
                    {
                        return parsedSettings;
                    }
                }
            }

            return Http2SettingsFrame.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract HTTP/2 settings frame.");
            return Http2SettingsFrame.Empty;
        }
    }

    public bool TryParseSettingsString(string? rawSettings, out Http2SettingsFrame settings)
    {
        settings = Http2SettingsFrame.Empty;

        if (string.IsNullOrWhiteSpace(rawSettings))
        {
            return false;
        }

        try
        {
            var raw = rawSettings.Trim();

            // Nếu là chuỗi Akamai Fingerprint tổng hợp: 1:65536;2:0;4:6291456;6:262144|15663105|0|m,a,s,p
            if (raw.Contains('|'))
            {
                var pipeIndex = raw.IndexOf('|');
                raw = raw[..pipeIndex].Trim();
            }

            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            uint? headerTableSize = null;
            uint? enablePush = null;
            uint? maxConcurrentStreams = null;
            uint? initialWindowSize = null;
            uint? maxFrameSize = null;
            uint? maxHeaderListSize = null;

            var pairs = raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries);
            var parsedAtLeastOne = false;

            foreach (var pair in pairs)
            {
                var trimmedPair = pair.Trim();
                var colonIndex = trimmedPair.IndexOf(':');
                if (colonIndex <= 0 || colonIndex >= trimmedPair.Length - 1)
                {
                    continue;
                }

                var idStr = trimmedPair[..colonIndex].Trim();
                var valStr = trimmedPair[(colonIndex + 1)..].Trim();

                if (!int.TryParse(idStr, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                    !uint.TryParse(valStr, NumberStyles.None, CultureInfo.InvariantCulture, out var val))
                {
                    continue;
                }

                parsedAtLeastOne = true;
                switch (id)
                {
                    case 1: // SETTINGS_HEADER_TABLE_SIZE
                        headerTableSize = val;
                        break;
                    case 2: // SETTINGS_ENABLE_PUSH
                        enablePush = val;
                        break;
                    case 3: // SETTINGS_MAX_CONCURRENT_STREAMS
                        maxConcurrentStreams = val;
                        break;
                    case 4: // SETTINGS_INITIAL_WINDOW_SIZE
                        initialWindowSize = val;
                        break;
                    case 5: // SETTINGS_MAX_FRAME_SIZE
                        maxFrameSize = val;
                        break;
                    case 6: // SETTINGS_MAX_HEADER_LIST_SIZE
                        maxHeaderListSize = val;
                        break;
                }
            }

            if (!parsedAtLeastOne)
            {
                return false;
            }

            settings = new Http2SettingsFrame
            {
                HeaderTableSize = headerTableSize,
                EnablePush = enablePush,
                MaxConcurrentStreams = maxConcurrentStreams,
                InitialWindowSize = initialWindowSize,
                MaxFrameSize = maxFrameSize,
                MaxHeaderListSize = maxHeaderListSize,
                RawSettings = raw,
                IsValid = true
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool IsSettingsConsistentWithBrowser(Http2SettingsFrame settings, string? userAgent, out string? anomalyReason)
    {
        anomalyReason = null;

        // Nếu frame rỗng hoặc không phân tích được -> Bỏ qua (không phạt nhầm)
        if (!settings.IsValid)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(userAgent))
        {
            anomalyReason = "Missing User-Agent while client sent HTTP/2 SETTINGS frame.";
            return false;
        }

        var ua = userAgent.AsSpan();

        // 1. Nhận diện gia đình Chrome / Chromium (Chrome, Edge, Brave, Opera)
        var isChromeFamily = (ContainsIgnoreCase(ua, "Chrome") || ContainsIgnoreCase(ua, "CriOS") || ContainsIgnoreCase(ua, "Edg/")) &&
                             !ContainsIgnoreCase(ua, "Firefox");

        if (isChromeFamily)
        {
            // Kiểm tra InitialWindowSize: Chrome luôn sử dụng 6291456 (6MB)
            if (settings.InitialWindowSize.HasValue)
            {
                var winSize = settings.InitialWindowSize.Value;
                if (winSize != ChromeExpectedInitialWindowSize)
                {
                    if (winSize == RfcDefaultInitialWindowSize)
                    {
                        anomalyReason = $"User-Agent claims Chrome, but HTTP/2 InitialWindowSize is {winSize} (RFC 7540 default - Python/cURL bot signature, expected {ChromeExpectedInitialWindowSize}).";
                        return false;
                    }

                    if (winSize == FirefoxInitialWindowSize)
                    {
                        anomalyReason = $"User-Agent claims Chrome, but HTTP/2 InitialWindowSize is {winSize} (matches Firefox signature - Impersonation).";
                        return false;
                    }

                    anomalyReason = $"User-Agent claims Chrome, but HTTP/2 InitialWindowSize is {winSize} (expected {ChromeExpectedInitialWindowSize}).";
                    return false;
                }
            }

            // Kiểm tra EnablePush: Chrome luôn đặt là 0
            if (settings.EnablePush.HasValue && settings.EnablePush.Value != 0)
            {
                anomalyReason = "User-Agent claims Chrome, but HTTP/2 EnablePush is enabled (expected 0).";
                return false;
            }

            return true;
        }

        // 2. Nhận diện gia đình Firefox
        var isFirefox = ContainsIgnoreCase(ua, "Firefox") || ContainsIgnoreCase(ua, "FxiOS");
        if (isFirefox)
        {
            if (settings.InitialWindowSize.HasValue && settings.InitialWindowSize.Value == ChromeExpectedInitialWindowSize)
            {
                anomalyReason = $"User-Agent claims Firefox, but HTTP/2 InitialWindowSize is {ChromeExpectedInitialWindowSize} (Chrome signature - Impersonation).";
                return false;
            }

            return true;
        }

        return true;
    }

    private static bool ContainsIgnoreCase(ReadOnlySpan<char> source, string value)
    {
        return source.Contains(value.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }
}
