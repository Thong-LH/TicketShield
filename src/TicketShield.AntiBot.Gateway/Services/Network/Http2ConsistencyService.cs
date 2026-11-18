using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Hiện thực dịch vụ đối chiếu tính nhất quán giao thức HTTP/2 (NET-13.1.2).
/// Hỗ trợ luật Grace Period cho môi trường Localhost/Dev không làm ảnh hưởng người dùng thật.
/// </summary>
public class Http2ConsistencyService(
    IPseudoHeaderInspector pseudoHeaderInspector,
    IHttp2SettingsInspector settingsInspector,
    ILogger<Http2ConsistencyService> logger) : IHttp2ConsistencyService
{
    public Http2Profile Inspect(HttpContext context, string? userAgent)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var isHttp2 = HttpProtocol.IsHttp2(context.Request.Protocol) || HttpProtocol.IsHttp3(context.Request.Protocol);

            // Bóc tách dữ liệu từ 2 bộ inspector
            var pseudoHeaderOrder = pseudoHeaderInspector.ExtractPseudoHeaderOrder(context);
            var settings = settingsInspector.ExtractSettings(context);

            var hasProxyFingerprint = !string.IsNullOrEmpty(pseudoHeaderOrder) || settings.IsValid;

            // =========================================================================
            // BƯỚC 1: LOCALHOST / DEV GRACE PERIOD
            // =========================================================================
            // Nếu là HTTP/1.1 thông thường và không có header fingerprint từ proxy -> Bỏ qua
            if (!isHttp2 && !hasProxyFingerprint)
            {
                return Http2Profile.DefaultNotApplicable;
            }

            // =========================================================================
            // BƯỚC 2: KIỂM TRA THỨ TỰ PSEUDO-HEADERS
            // =========================================================================
            if (!string.IsNullOrEmpty(pseudoHeaderOrder))
            {
                if (!pseudoHeaderInspector.IsOrderConsistentWithBrowser(pseudoHeaderOrder, userAgent, out var orderAnomaly))
                {
                    logger.LogWarning("HTTP/2 Pseudo-Header Inconsistency: {Reason}", orderAnomaly);
                    return new Http2Profile
                    {
                        IsHttp2Connection = true,
                        PseudoHeaderOrder = pseudoHeaderOrder,
                        Settings = settings,
                        Status = Http2InspectionStatus.Inconsistent,
                        NetworkInconsistencyScore = 1.0f,
                        AnomalyReason = orderAnomaly,
                        InspectedAtUtc = DateTime.UtcNow
                    };
                }
            }

            // =========================================================================
            // BƯỚC 3: KIỂM TRA HTTP/2 SETTINGS FRAME
            // =========================================================================
            if (settings.IsValid)
            {
                if (!settingsInspector.IsSettingsConsistentWithBrowser(settings, userAgent, out var settingsAnomaly))
                {
                    logger.LogWarning("HTTP/2 SETTINGS Frame Inconsistency: {Reason}", settingsAnomaly);
                    return new Http2Profile
                    {
                        IsHttp2Connection = true,
                        PseudoHeaderOrder = pseudoHeaderOrder,
                        Settings = settings,
                        Status = Http2InspectionStatus.Inconsistent,
                        NetworkInconsistencyScore = 1.0f,
                        AnomalyReason = settingsAnomaly,
                        InspectedAtUtc = DateTime.UtcNow
                    };
                }
            }

            // =========================================================================
            // BƯỚC 4: ĐỐI CHIẾU CHÉO VỚI TLS PROFILE (Kế thừa từ NET-13.1.1)
            // =========================================================================
            if (context.Items.TryGetValue("TlsProfile", out var tlsObj) && tlsObj is TlsProfile tlsProfile)
            {
                // Phát hiện tấn công Protocol Downgrade: TLS ALPN đàm phán h2 nhưng Application lại chạy HTTP/1.1
                if (tlsProfile.IsSecureConnection &&
                    tlsProfile.Alpn == "h2" &&
                    HttpProtocol.IsHttp11(context.Request.Protocol) &&
                    !hasProxyFingerprint)
                {
                    const string downgradeReason = "Protocol Downgrade detected: TLS ALPN negotiated 'h2' but client sent HTTP/1.1 request (Bypassing HTTP/2 Inspection).";
                    logger.LogWarning("{Reason}", downgradeReason);
                    return new Http2Profile
                    {
                        IsHttp2Connection = false,
                        PseudoHeaderOrder = pseudoHeaderOrder,
                        Settings = settings,
                        Status = Http2InspectionStatus.Inconsistent,
                        NetworkInconsistencyScore = 1.0f,
                        AnomalyReason = downgradeReason,
                        InspectedAtUtc = DateTime.UtcNow
                    };
                }
            }

            // =========================================================================
            // BƯỚC 5: TẤT CẢ TÍN HIỆU ĐỀU NHẤT QUÁN (CONSISTENT)
            // =========================================================================
            return new Http2Profile
            {
                IsHttp2Connection = isHttp2,
                PseudoHeaderOrder = pseudoHeaderOrder,
                Settings = settings,
                Status = Http2InspectionStatus.Consistent,
                NetworkInconsistencyScore = 0.0f,
                AnomalyReason = null,
                InspectedAtUtc = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in Http2ConsistencyService. Falling back to Grace Period.");
            return Http2Profile.DefaultNotApplicable;
        }
    }
}
