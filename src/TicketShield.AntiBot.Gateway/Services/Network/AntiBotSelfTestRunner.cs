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
        IHttp2ConsistencyService consistencyService,
        IClientHintsValidator? clientHintsValidator = null,
        ISubnetCorrelationTracker? subnetTracker = null,
        INetworkConsistencyService? networkService = null,
        INonceValidationService? nonceService = null)
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

        // =========================================================================
        // NET-13.1.3: CLIENT HINTS (Sec-Ch-Ua) & SUBNET CORRELATION TESTS
        // =========================================================================
        var hintsValidator = clientHintsValidator ?? new ClientHintsValidator();
        var subTracker = subnetTracker ?? new SubnetCorrelationTracker(null, null, null, Microsoft.Extensions.Logging.Abstractions.NullLogger<SubnetCorrelationTracker>.Instance);
        var netService = networkService ?? new NetworkConsistencyService(extractor, consistencyService, hintsValidator, subTracker, Microsoft.Extensions.Logging.Abstractions.NullLogger<NetworkConsistencyService>.Instance);

        // TC-15: Client Hints Chrome 130 Consistent (Score = 0.0)
        sw.Restart();
        var ctxCh1 = new DefaultHttpContext();
        ctxCh1.Request.Headers.UserAgent = ChromeUa;
        ctxCh1.Request.Headers["Sec-Ch-Ua"] = "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\", \"Not?A_Brand\";v=\"99\"";
        ctxCh1.Request.Headers["Sec-Ch-Ua-Mobile"] = "?0";
        ctxCh1.Request.Headers["Sec-Ch-Ua-Platform"] = "\"Windows\"";
        ctxCh1.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        ctxCh1.Request.Headers["Sec-Fetch-Mode"] = "cors";
        ctxCh1.Request.Headers["Sec-Fetch-Dest"] = "empty";
        var chRes1 = hintsValidator.Validate(ctxCh1);
        sw.Stop();
        var pass15 = chRes1.Status == ClientHintsStatus.Consistent && chRes1.InconsistencyScore == 0.0f;
        results.Add(new SelfTestResult(
            "TC-15: Client Hints Chrome 130 Consistent (Score=0.0)",
            pass15,
            $"Status={chRes1.Status}, Score={chRes1.InconsistencyScore}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-16: Client Hints Safari macOS True User (Score = 0.0, NotApplicable)
        sw.Restart();
        var ctxSafari = new DefaultHttpContext();
        ctxSafari.Request.Headers.UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15";
        var chResSafari = hintsValidator.Validate(ctxSafari);
        sw.Stop();
        var pass16 = chResSafari.Status == ClientHintsStatus.NotApplicable && chResSafari.InconsistencyScore == 0.0f;
        results.Add(new SelfTestResult(
            "TC-16: Client Hints Safari macOS True User (Score=0.0, NotApplicable)",
            pass16,
            $"Status={chResSafari.Status}, Score={chResSafari.InconsistencyScore}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-17: Client Hints Firefox Windows True User (Score = 0.0, NotApplicable)
        sw.Restart();
        var ctxFirefox = new DefaultHttpContext();
        ctxFirefox.Request.Headers.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0";
        var chResFirefox = hintsValidator.Validate(ctxFirefox);
        sw.Stop();
        var pass17 = chResFirefox.Status == ClientHintsStatus.NotApplicable && chResFirefox.InconsistencyScore == 0.0f;
        results.Add(new SelfTestResult(
            "TC-17: Client Hints Firefox Windows True User (Score=0.0, NotApplicable)",
            pass17,
            $"Status={chResFirefox.Status}, Score={chResFirefox.InconsistencyScore}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-18: Client Hints TicketShield Mobile App True User (Score = 0.0, NotApplicable)
        sw.Restart();
        var ctxMobileApp = new DefaultHttpContext();
        ctxMobileApp.Request.Headers.UserAgent = "TicketShield-Mobile/2.1.0 (Android 14; Pixel 8)";
        var chResMobileApp = hintsValidator.Validate(ctxMobileApp);
        sw.Stop();
        var pass18 = chResMobileApp.Status == ClientHintsStatus.NotApplicable && chResMobileApp.InconsistencyScore == 0.0f;
        results.Add(new SelfTestResult(
            "TC-18: Client Hints TicketShield Mobile App True User (Score=0.0)",
            pass18,
            $"Status={chResMobileApp.Status}, Score={chResMobileApp.InconsistencyScore}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-19: Bot curl_cffi Impersonation (Chrome UA without Sec-Ch-Ua -> Score = 1.0)
        sw.Restart();
        var ctxCurlCffi = new DefaultHttpContext();
        ctxCurlCffi.Request.Headers.UserAgent = ChromeUa;
        var chResCurlCffi = hintsValidator.Validate(ctxCurlCffi);
        sw.Stop();
        var pass19 = chResCurlCffi.Status == ClientHintsStatus.MissingHints && chResCurlCffi.InconsistencyScore == 1.0f;
        results.Add(new SelfTestResult(
            "TC-19: Bot curl_cffi Impersonation Missing Hints (Score=1.0)",
            pass19,
            $"Status={chResCurlCffi.Status}, Reason={chResCurlCffi.AnomalyReason}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-20: Bot Version Mismatch (Chrome 130 vs Sec-Ch-Ua v=120 -> Score = 1.0)
        sw.Restart();
        var ctxVerMismatch = new DefaultHttpContext();
        ctxVerMismatch.Request.Headers.UserAgent = ChromeUa;
        ctxVerMismatch.Request.Headers["Sec-Ch-Ua"] = "\"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"";
        ctxVerMismatch.Request.Headers["Sec-Ch-Ua-Mobile"] = "?0";
        ctxVerMismatch.Request.Headers["Sec-Ch-Ua-Platform"] = "\"Windows\"";
        ctxVerMismatch.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        var chResVerMismatch = hintsValidator.Validate(ctxVerMismatch);
        sw.Stop();
        var pass20 = chResVerMismatch.Status == ClientHintsStatus.VersionMismatch && chResVerMismatch.InconsistencyScore == 1.0f;
        results.Add(new SelfTestResult(
            "TC-20: Bot Version Mismatch (Chrome 130 vs Hints v=120 -> Score=1.0)",
            pass20,
            $"Status={chResVerMismatch.Status}, Reason={chResVerMismatch.AnomalyReason}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-21: Bot Platform Mismatch (Windows UA vs Linux Platform -> Score = 1.0)
        sw.Restart();
        var ctxPlatMismatch = new DefaultHttpContext();
        ctxPlatMismatch.Request.Headers.UserAgent = ChromeUa;
        ctxPlatMismatch.Request.Headers["Sec-Ch-Ua"] = "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\"";
        ctxPlatMismatch.Request.Headers["Sec-Ch-Ua-Platform"] = "\"Linux\"";
        ctxPlatMismatch.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        var chResPlatMismatch = hintsValidator.Validate(ctxPlatMismatch);
        sw.Stop();
        var pass21 = chResPlatMismatch.Status == ClientHintsStatus.PlatformMismatch && chResPlatMismatch.InconsistencyScore == 1.0f;
        results.Add(new SelfTestResult(
            "TC-21: Bot Platform Mismatch (Windows UA vs Linux -> Score=1.0)",
            pass21,
            $"Status={chResPlatMismatch.Status}, Reason={chResPlatMismatch.AnomalyReason}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-22: Safari Impersonator sending Chromium Hints -> Score = 1.0
        sw.Restart();
        var ctxSafariSpoof = new DefaultHttpContext();
        ctxSafariSpoof.Request.Headers.UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15";
        ctxSafariSpoof.Request.Headers["Sec-Ch-Ua"] = "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\"";
        var chResSafariSpoof = hintsValidator.Validate(ctxSafariSpoof);
        sw.Stop();
        var pass22 = chResSafariSpoof.Status == ClientHintsStatus.BrandMismatch && chResSafariSpoof.InconsistencyScore == 1.0f;
        results.Add(new SelfTestResult(
            "TC-22: Safari Impersonator Sending Chromium Hints (Score=1.0)",
            pass22,
            $"Status={chResSafariSpoof.Status}, Reason={chResSafariSpoof.AnomalyReason}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-23: Subnet Tracking Localhost 127.0.0.1 Exemption (Burst 50 req -> Score=0.0)
        sw.Restart();
        bool pass23 = true;
        for (int i = 0; i < 50; i++)
        {
            var res = subTracker.TrackSubnetAsync("127.0.0.1").GetAwaiter().GetResult();
            if (!res.IsExempt || res.IsAnomaly || res.InconsistencyScore > 0.0f)
            {
                pass23 = false;
                break;
            }
        }
        sw.Stop();
        results.Add(new SelfTestResult(
            "TC-23: Subnet Localhost 127.0.0.1 Exemption (50 bursts -> Score=0.0)",
            pass23,
            "Localhost IP is 100% exempt from rate limiting",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-24: Subnet Tracking Residential Proxy Botnet Cluster (/24 -> Score=1.0)
        sw.Restart();
        SubnetRateMetrics? clusterRes = null;
        for (int i = 1; i <= 16; i++)
        {
            clusterRes = subTracker.TrackSubnetAsync($"198.51.100.{i % 8 + 1}").GetAwaiter().GetResult();
        }
        sw.Stop();
        var pass24 = clusterRes != null && clusterRes.IsAnomaly && clusterRes.InconsistencyScore == 1.0f;
        results.Add(new SelfTestResult(
            "TC-24: Residential Proxy Botnet Cluster Detection (Score=1.0)",
            pass24,
            $"Subnet={clusterRes?.SubnetCidr}, ReqCount={clusterRes?.SubnetRequestCount}, DistinctIps={clusterRes?.DistinctIpCount}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-25: Facade NetworkConsistencyService End-to-End Evaluation
        sw.Restart();
        var ctxFacade = new DefaultHttpContext();
        ctxFacade.Request.Headers.UserAgent = ChromeUa;
        ctxFacade.Request.Headers["Sec-Ch-Ua"] = "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\"";
        ctxFacade.Request.Headers["Sec-Ch-Ua-Mobile"] = "?0";
        ctxFacade.Request.Headers["Sec-Ch-Ua-Platform"] = "\"Windows\"";
        ctxFacade.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        ctxFacade.Request.Headers["Sec-Fetch-Mode"] = "cors";
        ctxFacade.Request.Headers["Sec-Fetch-Dest"] = "empty";
        ctxFacade.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        var summary = netService.EvaluateAsync(ctxFacade).GetAwaiter().GetResult();
        sw.Stop();
        var pass25 = summary.OverallInconsistencyScore == 0.0f && !summary.HasInconsistency;
        results.Add(new SelfTestResult(
            "TC-25: Facade NetworkConsistencyService End-to-End (Score=0.0)",
            pass25,
            $"OverallScore={summary.OverallInconsistencyScore}, HasInconsistency={summary.HasInconsistency}",
            sw.Elapsed.TotalMilliseconds * 1000.0));

        // TC-26: Benchmark 50,000 Client Hints Validations (SLA < 10µs)
        sw.Restart();
        for (var i = 0; i < benchmarkCount; i++)
        {
            hintsValidator.Validate(ctxCh1);
        }
        sw.Stop();
        var avgChBenchmarkUs = (sw.Elapsed.TotalMilliseconds * 1000.0) / benchmarkCount;
        var pass26 = avgChBenchmarkUs < 10.0;
        results.Add(new SelfTestResult(
            $"TC-26: Benchmark {benchmarkCount:N0} Client Hints Validations (SLA < 10µs)",
            pass26,
            $"Completed in {sw.ElapsedMilliseconds}ms, Avg = {avgChBenchmarkUs:F4} µs/op",
            avgChBenchmarkUs));

        // =========================================================================
        // SDK-13.2.3: ONE-TIME CRYPTOGRAPHIC NONCE & ANTI-REPLAY TESTS
        // =========================================================================
        if (nonceService != null)
        {
            // TC-27: Nonce First-Time Registration & Atomic Consumption
            sw.Restart();
            var n1 = Guid.NewGuid().ToString("N");
            var t1 = Guid.NewGuid();
            nonceService.RegisterNonceAsync(n1, t1, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            var res1 = nonceService.ValidateAndConsumeNonceAsync(n1, t1).GetAwaiter().GetResult();
            sw.Stop();
            var pass27 = res1.IsSuccess && !res1.IsReplay && !res1.IsExpired;
            results.Add(new SelfTestResult(
                "TC-27: One-Time Nonce First-Time Registration & Consumption",
                pass27,
                $"IsSuccess={res1.IsSuccess}, IsReplay={res1.IsReplay}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-28: Nonce Replay Attack Detection (Same Nonce Consumed Second Time)
            sw.Restart();
            var res2 = nonceService.ValidateAndConsumeNonceAsync(n1, t1).GetAwaiter().GetResult();
            sw.Stop();
            var pass28 = !res2.IsSuccess && res2.IsReplay && !res2.IsExpired;
            results.Add(new SelfTestResult(
                "TC-28: Nonce Replay Attack Blocked (Double-Claim Detection)",
                pass28,
                $"IsSuccess={res2.IsSuccess}, IsReplay={res2.IsReplay}, Msg={res2.Message}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-29: Unknown / Expired Nonce Handling
            sw.Restart();
            var fakeNonce = Guid.NewGuid().ToString("N");
            var res3 = nonceService.ValidateAndConsumeNonceAsync(fakeNonce, t1).GetAwaiter().GetResult();
            sw.Stop();
            var pass29 = !res3.IsSuccess && res3.IsExpired;
            results.Add(new SelfTestResult(
                "TC-29: Unknown Nonce Rejected (IsExpired = True)",
                pass29,
                $"IsSuccess={res3.IsSuccess}, IsExpired={res3.IsExpired}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-30: Tier Mismatch Nonce Validation
            sw.Restart();
            var n4 = Guid.NewGuid().ToString("N");
            var t4A = Guid.NewGuid();
            var t4B = Guid.NewGuid();
            nonceService.RegisterNonceAsync(n4, t4A, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            var res4 = nonceService.ValidateAndConsumeNonceAsync(n4, t4B).GetAwaiter().GetResult();
            sw.Stop();
            var pass30 = !res4.IsSuccess && !res4.IsReplay && !res4.IsExpired;
            results.Add(new SelfTestResult(
                "TC-30: Nonce Tier Mismatch Rejected",
                pass30,
                $"IsSuccess={res4.IsSuccess}, Msg={res4.Message}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-31: Benchmark 10,000 Nonce Consumptions (SLA < 20µs)
            sw.Restart();
            const int nonceBenchCount = 10_000;
            for (var i = 0; i < nonceBenchCount; i++)
            {
                var nBench = Guid.NewGuid().ToString("N");
                nonceService.RegisterNonceAsync(nBench, t1, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
                nonceService.ValidateAndConsumeNonceAsync(nBench, t1).GetAwaiter().GetResult();
            }
            sw.Stop();
            var avgNonceBenchmarkUs = (sw.Elapsed.TotalMilliseconds * 1000.0) / (nonceBenchCount * 2);
            var pass31 = avgNonceBenchmarkUs < 20.0;
            results.Add(new SelfTestResult(
                $"TC-31: Benchmark {nonceBenchCount:N0} Nonce Cycles (SLA < 20µs)",
                pass31,
                $"Completed in {sw.ElapsedMilliseconds}ms, Avg = {avgNonceBenchmarkUs:F4} µs/op",
                avgNonceBenchmarkUs));
        }

        var allPassed = results.All(r => r.Passed);
        var passedCount = results.Count(r => r.Passed);

        var overallAvgBenchmarkUs = (avgH2BenchmarkUs + avgChBenchmarkUs) / 2.0;

        return new SelfTestSuiteReport(
            allPassed,
            results.Count,
            passedCount,
            overallAvgBenchmarkUs,
            results);
    }
}
