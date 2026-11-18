using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

public record SelfTestResult(
    string TestName,
    bool Passed,
    string Details,
    double LatencyMicroseconds
);

public record SelfTestSuiteReport(
    bool AllPassed,
    int TotalTests,
    int PassedTests,
    double AverageLatencyMicroseconds,
    IReadOnlyList<SelfTestResult> Results
);

/// <summary>
/// Service kiểm thử tự động nội tại (Self-Test Runner) cho tầng Network Consistency Layer (NET-13.1.1 & NET-13.1.2).
/// Thực thi toàn bộ test matrix in-memory và benchmark độ trễ microsecond.
/// </summary>
public static class AntiBotSelfTestRunner
{
    private const string ChromeUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    public static SelfTestSuiteReport RunAllTests(
        IJa4Parser parser,
        ITlsProfileExtractor extractor,
        IHttp2ConsistencyService consistencyService)
    {
        var results = new List<SelfTestResult>();
        var sw = new Stopwatch();

        // =========================================================================
        // NET-13.1.1: TLS PROFILE & JA4 TESTS
        // =========================================================================

        // TC-01: Chrome 130 Standard JA4
        sw.Restart();
        const string chromeJa4 = "t13d1516h2_8daaf6152771_02711d0e519e";
        var ok1 = parser.TryParse(chromeJa4, out var parsed1);
        sw.Stop();
        var pass1 = ok1 && parsed1.IsValid && parsed1.Protocol == 't' && parsed1.TlsVersion == "13" &&
                    parsed1.Alpn == "h2" && parsed1.CipherCount == 15 && parsed1.ExtensionCount == 16 &&
                    parsed1.CipherHash == "8daaf6152771";
        results.Add(new SelfTestResult(
            "TC-01: Chrome 130 Standard JA4 Parsing",
            pass1,
            $"Protocol={parsed1.Protocol}, Version={parsed1.TlsVersion}, ALPN={parsed1.Alpn}, Ciphers={parsed1.CipherCount}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-02: QUIC / HTTP3 JA4
        sw.Restart();
        const string quicJa4 = "q13d1012h3_aabbccddeeff_112233445566";
        var ok2 = parser.TryParse(quicJa4, out var parsed2);
        sw.Stop();
        var pass2 = ok2 && parsed2.IsValid && parsed2.Protocol == 'q' && parsed2.Alpn == "h3";
        results.Add(new SelfTestResult(
            "TC-02: QUIC / HTTP3 JA4 Parsing",
            pass2,
            $"Protocol={parsed2.Protocol}, ALPN={parsed2.Alpn}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-03: HTTP 1.1 with IP SNI
        sw.Restart();
        const string http1Ja4 = "t12i0508h1_c02bc02f009e_000000000000";
        var ok3 = parser.TryParse(http1Ja4, out var parsed3);
        sw.Stop();
        var pass3 = ok3 && parsed3.IsValid && parsed3.SniType == 'i' && parsed3.TlsVersion == "12" && parsed3.Alpn == "h1";
        results.Add(new SelfTestResult(
            "TC-03: HTTP 1.1 IP SNI JA4 Parsing",
            pass3,
            $"SNI={parsed3.SniType}, Version={parsed3.TlsVersion}, ALPN={parsed3.Alpn}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-04: Malformed JA4 (Fail-Safe)
        sw.Restart();
        var ok4 = parser.TryParse("malformed_ja4_123", out var parsed4);
        sw.Stop();
        var pass4 = !ok4 && !parsed4.IsValid;
        results.Add(new SelfTestResult(
            "TC-04: Malformed JA4 Fail-Safe Handling",
            pass4,
            $"Expected=False, Actual={ok4}, IsValid={parsed4.IsValid}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-05: TlsProfileExtractor with X-JA4 Header
        sw.Restart();
        var ctxProxy = new DefaultHttpContext();
        ctxProxy.Request.Headers["X-JA4"] = chromeJa4;
        var profileProxy = extractor.Extract(ctxProxy);
        sw.Stop();
        var pass5 = profileProxy.Source == TlsSourceType.ProxyHeaderJa4 &&
                    profileProxy.IsSecureConnection &&
                    profileProxy.TlsVersion == "Tls13" &&
                    profileProxy.Alpn == "h2" &&
                    profileProxy.Ja4 != null;
        results.Add(new SelfTestResult(
            "TC-05: TlsProfileExtractor with X-JA4 Header",
            pass5,
            $"Source={profileProxy.Source}, Version={profileProxy.TlsVersion}, ALPN={profileProxy.Alpn}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-06: TlsProfileExtractor with CF-JA4 (Cloudflare)
        sw.Restart();
        var ctxCf = new DefaultHttpContext();
        ctxCf.Request.Headers["CF-JA4"] = http1Ja4;
        var profileCf = extractor.Extract(ctxCf);
        sw.Stop();
        var pass6 = profileCf.Source == TlsSourceType.ProxyHeaderJa4 &&
                    profileCf.TlsVersion == "Tls12" &&
                    profileCf.Alpn == "http/1.1";
        results.Add(new SelfTestResult(
            "TC-06: TlsProfileExtractor with CF-JA4 (Cloudflare)",
            pass6,
            $"Source={profileCf.Source}, Version={profileCf.TlsVersion}, ALPN={profileCf.Alpn}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-07: TlsProfileExtractor Plain HTTP Non-TLS
        sw.Restart();
        var ctxPlain = new DefaultHttpContext();
        ctxPlain.Request.Scheme = "http";
        var profilePlain = extractor.Extract(ctxPlain);
        sw.Stop();
        var pass7 = profilePlain.Source == TlsSourceType.None && !profilePlain.IsSecureConnection;
        results.Add(new SelfTestResult(
            "TC-07: TlsProfileExtractor Plain HTTP Non-TLS",
            pass7,
            $"Source={profilePlain.Source}, IsSecure={profilePlain.IsSecureConnection}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-08: Benchmark 50,000 JA4 Parse Operations
        const int benchmarkCount = 50_000;
        sw.Restart();
        for (var i = 0; i < benchmarkCount; i++)
        {
            parser.TryParse(chromeJa4, out _);
        }
        sw.Stop();
        var avgBenchmarkUs = (sw.Elapsed.TotalMilliseconds * 1000.0) / benchmarkCount;
        var pass8 = avgBenchmarkUs < 50.0;
        results.Add(new SelfTestResult(
            $"TC-08: Benchmark {benchmarkCount:N0} JA4 Parses (SLA < 50µs)",
            pass8,
            $"Completed in {sw.ElapsedMilliseconds}ms, Avg = {avgBenchmarkUs:F4} µs/op",
            avgBenchmarkUs));

        // =========================================================================
        // NET-13.1.2: HTTP/2 SETTINGS & PSEUDO-HEADERS TESTS
        // =========================================================================

        // TC-09: HTTP/2 Chrome 130 Standard Consistency (m,a,s,p + 6MB Window)
        sw.Restart();
        var ctxH2Chrome = new DefaultHttpContext();
        ctxH2Chrome.Request.Protocol = "HTTP/2";
        ctxH2Chrome.Request.Headers["X-Http2-Pseudo-Headers"] = "m,a,s,p";
        ctxH2Chrome.Request.Headers["X-Http2-Settings"] = "1:65536;2:0;4:6291456;6:262144";
        var h2ResChrome = consistencyService.Inspect(ctxH2Chrome, ChromeUa);
        sw.Stop();
        var pass9 = h2ResChrome.Status == Http2InspectionStatus.Consistent &&
                    h2ResChrome.NetworkInconsistencyScore == 0.0f;
        results.Add(new SelfTestResult(
            "TC-09: HTTP/2 Chrome 130 Standard Consistency",
            pass9,
            $"Status={h2ResChrome.Status}, Score={h2ResChrome.NetworkInconsistencyScore}, Order={h2ResChrome.PseudoHeaderOrder}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-10: HTTP/2 Bot Pseudo-Header Mismatch (Chrome UA + Firefox order m,p,a,s)
        sw.Restart();
        var ctxH2MismatchOrder = new DefaultHttpContext();
        ctxH2MismatchOrder.Request.Protocol = "HTTP/2";
        ctxH2MismatchOrder.Request.Headers["X-Http2-Pseudo-Headers"] = "m,p,a,s"; // Firefox order
        ctxH2MismatchOrder.Request.Headers["X-Http2-Settings"] = "1:65536;2:0;4:6291456;6:262144";
        var h2ResMismatchOrder = consistencyService.Inspect(ctxH2MismatchOrder, ChromeUa);
        sw.Stop();
        var pass10 = h2ResMismatchOrder.Status == Http2InspectionStatus.Inconsistent &&
                     h2ResMismatchOrder.NetworkInconsistencyScore == 1.0f &&
                     h2ResMismatchOrder.AnomalyReason != null &&
                     h2ResMismatchOrder.AnomalyReason.Contains("Firefox");
        results.Add(new SelfTestResult(
            "TC-10: HTTP/2 Bot Pseudo-Header Order Mismatch (Score=1.0)",
            pass10,
            $"Score={h2ResMismatchOrder.NetworkInconsistencyScore}, Reason={h2ResMismatchOrder.AnomalyReason}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-11: HTTP/2 Bot Settings Mismatch (Chrome UA + Python 65535 Window)
        sw.Restart();
        var ctxH2MismatchSettings = new DefaultHttpContext();
        ctxH2MismatchSettings.Request.Protocol = "HTTP/2";
        ctxH2MismatchSettings.Request.Headers["X-Http2-Pseudo-Headers"] = "m,a,s,p";
        ctxH2MismatchSettings.Request.Headers["X-Http2-Settings"] = "1:65536;2:0;4:65535;6:262144"; // 65535 = RFC Python bot
        var h2ResMismatchSettings = consistencyService.Inspect(ctxH2MismatchSettings, ChromeUa);
        sw.Stop();
        var pass11 = h2ResMismatchSettings.Status == Http2InspectionStatus.Inconsistent &&
                     h2ResMismatchSettings.NetworkInconsistencyScore == 1.0f &&
                     h2ResMismatchSettings.AnomalyReason != null &&
                     h2ResMismatchSettings.AnomalyReason.Contains("RFC 7540");
        results.Add(new SelfTestResult(
            "TC-11: HTTP/2 Bot Settings Window Mismatch (Score=1.0)",
            pass11,
            $"Score={h2ResMismatchSettings.NetworkInconsistencyScore}, Reason={h2ResMismatchSettings.AnomalyReason}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-12: HTTP/2 Protocol Downgrade Attack (TLS ALPN h2 -> Application HTTP/1.1)
        sw.Restart();
        var ctxDowngrade = new DefaultHttpContext();
        ctxDowngrade.Request.Protocol = "HTTP/1.1";
        ctxDowngrade.Items["TlsProfile"] = new TlsProfile
        {
            IsSecureConnection = true,
            Alpn = "h2",
            TlsVersion = "Tls13"
        };
        var h2ResDowngrade = consistencyService.Inspect(ctxDowngrade, ChromeUa);
        sw.Stop();
        var pass12 = h2ResDowngrade.Status == Http2InspectionStatus.Inconsistent &&
                     h2ResDowngrade.NetworkInconsistencyScore == 1.0f &&
                     h2ResDowngrade.AnomalyReason != null &&
                     h2ResDowngrade.AnomalyReason.Contains("Protocol Downgrade");
        results.Add(new SelfTestResult(
            "TC-12: HTTP/2 Protocol Downgrade Attack Detection (Score=1.0)",
            pass12,
            $"Score={h2ResDowngrade.NetworkInconsistencyScore}, Reason={h2ResDowngrade.AnomalyReason}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-13: HTTP/2 Localhost Grace Period (Plain HTTP/1.1 -> No Score Penalty)
        sw.Restart();
        var ctxGrace = new DefaultHttpContext();
        ctxGrace.Request.Protocol = "HTTP/1.1";
        ctxGrace.Request.Scheme = "http";
        var h2ResGrace = consistencyService.Inspect(ctxGrace, ChromeUa);
        sw.Stop();
        var pass13 = h2ResGrace.Status == Http2InspectionStatus.NotApplicable &&
                     h2ResGrace.NetworkInconsistencyScore == 0.0f;
        results.Add(new SelfTestResult(
            "TC-13: HTTP/2 Localhost Dev Grace Period (Score=0.0)",
            pass13,
            $"Status={h2ResGrace.Status}, Score={h2ResGrace.NetworkInconsistencyScore}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-14: Benchmark 50,000 HTTP/2 Consistency Inspections
        sw.Restart();
        for (var i = 0; i < benchmarkCount; i++)
        {
            consistencyService.Inspect(ctxH2Chrome, ChromeUa);
        }
        sw.Stop();
        var avgH2BenchmarkUs = (sw.Elapsed.TotalMilliseconds * 1000.0) / benchmarkCount;
        var pass14 = avgH2BenchmarkUs < 50.0;
        results.Add(new SelfTestResult(
            $"TC-14: Benchmark {benchmarkCount:N0} HTTP/2 Inspections (SLA < 50µs)",
            pass14,
            $"Completed in {sw.ElapsedMilliseconds}ms, Avg = {avgH2BenchmarkUs:F4} µs/op",
            avgH2BenchmarkUs));

        var allPassed = results.All(r => r.Passed);
        var passedCount = results.Count(r => r.Passed);

        return new SelfTestSuiteReport(
            allPassed,
            results.Count,
            passedCount,
            avgH2BenchmarkUs,
            results);
    }
}
