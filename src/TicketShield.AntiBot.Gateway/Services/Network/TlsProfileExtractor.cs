using System.Security.Authentication;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Hiện thực trích xuất hồ sơ TLS Profile hỗ trợ cả mô hình Direct Kestrel Termination và Reverse Proxy Offload.
/// </summary>
public class TlsProfileExtractor(
    IJa4Parser ja4Parser,
    ILogger<TlsProfileExtractor> logger) : ITlsProfileExtractor
{
    private static readonly string[] Ja4HeaderCandidates =
    [
        "X-JA4",
        "CF-JA4",
        "X-Forwarded-Tls-Ja4"
    ];

    public TlsProfile Extract(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            // =========================================================================
            // BƯỚC 1: ƯU TIÊN HEADER REVERSE PROXY NGOÀI (X-JA4, CF-JA4)
            // =========================================================================
            foreach (var headerName in Ja4HeaderCandidates)
            {
                if (context.Request.Headers.TryGetValue(headerName, out var headerValues) &&
                    !string.IsNullOrWhiteSpace(headerValues.FirstOrDefault()))
                {
                    var rawJa4 = headerValues.FirstOrDefault();
                    if (ja4Parser.TryParse(rawJa4, out var parsedJa4) && parsedJa4.IsValid)
                    {
                        return new TlsProfile
                        {
                            TlsVersion = MapJa4TlsVersion(parsedJa4.TlsVersion),
                            NegotiatedCipherSuite = null, // Proxy JA4 chỉ cung cấp hash ciphers
                            Alpn = MapJa4Alpn(parsedJa4.Alpn),
                            Ja4 = parsedJa4,
                            Source = TlsSourceType.ProxyHeaderJa4,
                            IsSecureConnection = true,
                            ExtractedAtUtc = DateTime.UtcNow
                        };
                    }

                    logger.LogWarning("Invalid or malformed JA4 header encountered in {HeaderName}: '{RawJa4}'",
                        headerName, rawJa4);
                }
            }

            // =========================================================================
            // BƯỚC 2: TRÍCH XUẤT TRỰC TIẾP TỪ KESTREL (ITlsHandshakeFeature)
            // =========================================================================
            var handshakeFeature = context.Features.Get<ITlsHandshakeFeature>();
            if (handshakeFeature != null)
            {
                var tlsVersion = MapSslProtocols(handshakeFeature.Protocol);
                var cipherSuite = ResolveCipherSuite(handshakeFeature);
                var alpn = ResolveKestrelAlpn(context.Request.Protocol);

                return new TlsProfile
                {
                    TlsVersion = tlsVersion,
                    NegotiatedCipherSuite = cipherSuite,
                    Alpn = alpn,
                    Ja4 = null,
                    Source = TlsSourceType.KestrelDirect,
                    IsSecureConnection = true,
                    ExtractedAtUtc = DateTime.UtcNow
                };
            }

            // =========================================================================
            // BƯỚC 3: FALLBACK NON-TLS / UNENCRYPTED CONNECTION
            // =========================================================================
            if (context.Request.IsHttps)
            {
                // Request qua HTTPS nhưng không có handshake feature (e.g. Test / In-memory test server)
                return new TlsProfile
                {
                    TlsVersion = "HttpsUnknown",
                    NegotiatedCipherSuite = null,
                    Alpn = ResolveKestrelAlpn(context.Request.Protocol),
                    Ja4 = null,
                    Source = TlsSourceType.None,
                    IsSecureConnection = true,
                    ExtractedAtUtc = DateTime.UtcNow
                };
            }

            return TlsProfile.NonTls;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error occurred while extracting TLS profile. Falling back to NonTls.");
            return TlsProfile.NonTls;
        }
    }

    private static string MapJa4TlsVersion(string rawVersion) => rawVersion switch
    {
        "13" => "Tls13",
        "12" => "Tls12",
        "11" => "Tls11",
        "10" => "Tls10",
        "s3" => "Ssl3",
        _ => $"Tls{rawVersion}"
    };

    private static string? MapJa4Alpn(string rawAlpn) => rawAlpn switch
    {
        "h2" => "h2",
        "h1" => "http/1.1",
        "h3" => "h3",
        "00" => null,
        _ => rawAlpn
    };

    private static string ResolveKestrelAlpn(string protocol)
    {
        if (HttpProtocol.IsHttp2(protocol)) return "h2";
        if (HttpProtocol.IsHttp3(protocol)) return "h3";
        if (HttpProtocol.IsHttp11(protocol)) return "http/1.1";
        return protocol;
    }

    private static string MapSslProtocols(SslProtocols protocol)
    {
#pragma warning disable SYSLIB0039 // Obsolete TLS versions are mapped intentionally for bot fingerprinting
        return protocol switch
        {
            SslProtocols.Tls13 => "Tls13",
            SslProtocols.Tls12 => "Tls12",
            SslProtocols.Tls11 => "Tls11",
            SslProtocols.Tls => "Tls10",
            _ => protocol.ToString()
        };
#pragma warning restore SYSLIB0039
    }

    private static string? ResolveCipherSuite(ITlsHandshakeFeature feature)
    {
        // Trên Linux/macOS hoặc .NET 7/8 OpenSSL
        if (feature.NegotiatedCipherSuite.HasValue)
        {
            return feature.NegotiatedCipherSuite.Value.ToString();
        }

        // Fallback trên Windows Schannel
        if (feature.CipherAlgorithm != System.Security.Authentication.CipherAlgorithmType.None)
        {
            return $"{feature.CipherAlgorithm}_{feature.CipherStrength}";
        }

        return null;
    }
}
