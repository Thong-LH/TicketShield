using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
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
/// Service kiểm thử tự động nội tại (Self-Test Runner) cho tầng Network Consistency Layer (NET-13.1.1).
/// Thực thi toàn bộ test matrix in-memory và benchmark độ trễ microsecond.
/// </summary>
public static class AntiBotSelfTestRunner
{
    public static SelfTestSuiteReport RunAllTests(IJa4Parser parser, ITlsProfileExtractor extractor)
    {
        var results = new List<SelfTestResult>();
        var sw = new Stopwatch();

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

        // TC-08: Benchmark 50,000 Iterations
        const int benchmarkCount = 50_000;
        sw.Restart();
        for (var i = 0; i < benchmarkCount; i++)
        {
            parser.TryParse(chromeJa4, out _);
        }
        sw.Stop();
        var avgBenchmarkUs = (sw.Elapsed.TotalMilliseconds * 1000.0) / benchmarkCount;
        var pass8 = avgBenchmarkUs < 50.0; // < 0.05ms (50µs) SLA target
        results.Add(new SelfTestResult(
            $"TC-08: Benchmark {benchmarkCount:N0} Iterations (SLA < 50µs)",
            pass8,
            $"Completed in {sw.ElapsedMilliseconds}ms, Avg = {avgBenchmarkUs:F4} µs/op",
            avgBenchmarkUs));

        var allPassed = results.All(r => r.Passed);
        var passedCount = results.Count(r => r.Passed);

        return new SelfTestSuiteReport(
            allPassed,
            results.Count,
            passedCount,
            avgBenchmarkUs,
            results);
    }
}
