using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using TicketShield.AntiBot.Gateway.Middleware;
using TicketShield.AntiBot.Gateway.Models;
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
        INonceValidationService? nonceService = null,
        ISessionTokenService? tokenService = null,
        IUserAgentCrossValidator? uaValidator = null,
        IAntiBotScoringService? scoringService = null)
    {
        var results = new List<SelfTestResult>();
        var sw = new Stopwatch();
        uaValidator ??= new UserAgentCrossValidator();

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

        // =========================================================================
        // SDK-13.2.3: MIDDLEWARE INTEGRATION ANTI-REPLAY TESTS
        // =========================================================================
        if (nonceService != null && tokenService != null && networkService != null)
        {
            var dummyGuard = new DummySelfTestGuardService();
            var middlewareLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AntiBotCheckoutGuardMiddleware>.Instance;
            var middleware = new AntiBotCheckoutGuardMiddleware((_) => Task.CompletedTask, middlewareLogger);

            var tierId = Guid.NewGuid();
            var (validToken, validNonce, _, _) = tokenService.GenerateSessionTokenWithNonce(tierId, null, 300);
            nonceService.RegisterNonceAsync(validNonce, tierId, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();

            // TC-32: Middleware Allow Valid Request -> Nonce Consumed & Forwarded with Header
            sw.Restart();
            var ctxValid = CreateCheckoutContext(validToken, validNonce, tierId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            middleware.InvokeAsync(ctxValid, dummyGuard, networkService, tokenService, nonceService, uaValidator).GetAwaiter().GetResult();
            sw.Stop();
            var pass32 = ctxValid.Response.StatusCode == StatusCodes.Status200OK
                         && ctxValid.Request.Headers["X-AntiBot-Nonce-Verified"] == "True"
                         && ctxValid.Request.Headers["X-AntiBot-Nonce"] == validNonce;
            results.Add(new SelfTestResult(
                "TC-32: Middleware Flow - Valid Nonce Forwarded with Verified Header",
                pass32,
                $"Status={ctxValid.Response.StatusCode}, VerifiedHeader={ctxValid.Request.Headers["X-AntiBot-Nonce-Verified"]}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-33: Middleware Block Replay Attack (Replay the exact same Token and Nonce of TC-32)
            sw.Restart();
            var ctxReplay = CreateCheckoutContext(validToken, validNonce, tierId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            middleware.InvokeAsync(ctxReplay, dummyGuard, networkService, tokenService, nonceService, uaValidator).GetAwaiter().GetResult();
            sw.Stop();
            var pass33 = ctxReplay.Response.StatusCode == StatusCodes.Status429TooManyRequests;
            results.Add(new SelfTestResult(
                "TC-33: Middleware Flow - Replay Attack Blocked (HTTP 429)",
                pass33,
                $"Expected=429, Actual={ctxReplay.Response.StatusCode}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-34: Middleware Block Nonce Mismatch (Token Nonce != Telemetry Nonce)
            sw.Restart();
            var (tokenMismatched, serverNonce, _, _) = tokenService.GenerateSessionTokenWithNonce(tierId, null, 300);
            nonceService.RegisterNonceAsync(serverNonce, tierId, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            var ctxMismatch = CreateCheckoutContext(tokenMismatched, "forged_telemetry_nonce_abc", tierId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            middleware.InvokeAsync(ctxMismatch, dummyGuard, networkService, tokenService, nonceService, uaValidator).GetAwaiter().GetResult();
            sw.Stop();
            var pass34 = ctxMismatch.Response.StatusCode == StatusCodes.Status403Forbidden;
            results.Add(new SelfTestResult(
                "TC-34: Middleware Flow - Nonce Mismatch Blocked (HTTP 403)",
                pass34,
                $"Expected=403, Actual={ctxMismatch.Response.StatusCode}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-35: Middleware Block Clock Skew Anomaly (> 15s)
            sw.Restart();
            var (tokenSkew, skewNonce, _, _) = tokenService.GenerateSessionTokenWithNonce(tierId, null, 300);
            nonceService.RegisterNonceAsync(skewNonce, tierId, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            var skewedTimestamp = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds(); // 30s in the future
            var ctxSkew = CreateCheckoutContext(tokenSkew, skewNonce, tierId, skewedTimestamp);
            middleware.InvokeAsync(ctxSkew, dummyGuard, networkService, tokenService, nonceService, uaValidator).GetAwaiter().GetResult();
            sw.Stop();
            var pass35 = ctxSkew.Response.StatusCode == StatusCodes.Status403Forbidden;
            results.Add(new SelfTestResult(
                "TC-35: Middleware Flow - Clock Skew Anomaly Blocked (HTTP 403)",
                pass35,
                $"Expected=403, Actual={ctxSkew.Response.StatusCode}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // =========================================================================
            // US-13.2.4: USER-AGENT CROSS-VALIDATION TESTS (TC-36 to TC-40)
            // =========================================================================

            // TC-36: Middleware Flow - Consistent Chrome Windows UA Cross-Validation (HTTP 200)
            sw.Restart();
            var (token36, nonce36, _, _) = tokenService.GenerateSessionTokenWithNonce(tierId, null, 300);
            nonceService.RegisterNonceAsync(nonce36, tierId, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            var ctx36 = CreateCheckoutContext(
                token36,
                nonce36,
                tierId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                userAgent: ChromeUa,
                clientUserAgent: ChromeUa);
            middleware.InvokeAsync(ctx36, dummyGuard, networkService, tokenService, nonceService, uaValidator).GetAwaiter().GetResult();
            sw.Stop();
            var pass36 = ctx36.Response.StatusCode == StatusCodes.Status200OK
                         && ctx36.Request.Headers["X-AntiBot-UA-Consistent"] == "True";
            results.Add(new SelfTestResult(
                "TC-36: Middleware Flow - Consistent Chrome Windows UA Cross-Validation (HTTP 200)",
                pass36,
                $"Status={ctx36.Response.StatusCode}, ConsistentHeader={ctx36.Request.Headers["X-AntiBot-UA-Consistent"]}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-37: Middleware Flow - Blatant OS Mismatch Blocked (Client Windows vs Linux VPS -> HTTP 403)
            sw.Restart();
            var (token37, nonce37, _, _) = tokenService.GenerateSessionTokenWithNonce(tierId, null, 300);
            nonceService.RegisterNonceAsync(nonce37, tierId, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            const string linuxHeaderUa = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
            var ctx37 = CreateCheckoutContext(
                token37,
                nonce37,
                tierId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                userAgent: linuxHeaderUa,
                clientUserAgent: ChromeUa);
            middleware.InvokeAsync(ctx37, dummyGuard, networkService, tokenService, nonceService, uaValidator).GetAwaiter().GetResult();
            sw.Stop();
            var pass37 = ctx37.Response.StatusCode == StatusCodes.Status403Forbidden;
            results.Add(new SelfTestResult(
                "TC-37: Middleware Flow - Blatant OS Mismatch Blocked (Client Windows vs Linux VPS -> HTTP 403)",
                pass37,
                $"Expected=403, Actual={ctx37.Response.StatusCode}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-38: Middleware Flow - Blatant Browser Mismatch Blocked (Client Chrome vs Firefox -> HTTP 403)
            sw.Restart();
            var (token38, nonce38, _, _) = tokenService.GenerateSessionTokenWithNonce(tierId, null, 300);
            nonceService.RegisterNonceAsync(nonce38, tierId, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            const string firefoxHeaderUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0";
            var ctx38 = CreateCheckoutContext(
                token38,
                nonce38,
                tierId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                userAgent: firefoxHeaderUa,
                clientUserAgent: ChromeUa);
            middleware.InvokeAsync(ctx38, dummyGuard, networkService, tokenService, nonceService, uaValidator).GetAwaiter().GetResult();
            sw.Stop();
            var pass38 = ctx38.Response.StatusCode == StatusCodes.Status403Forbidden;
            results.Add(new SelfTestResult(
                "TC-38: Middleware Flow - Blatant Browser Mismatch Blocked (Client Chrome vs Firefox -> HTTP 403)",
                pass38,
                $"Expected=403, Actual={ctx38.Response.StatusCode}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-39: Middleware Flow - In-App WebView Normalization (FB/Zalo Trailing Tokens -> HTTP 200)
            sw.Restart();
            var (token39, nonce39, _, _) = tokenService.GenerateSessionTokenWithNonce(tierId, null, 300);
            nonceService.RegisterNonceAsync(nonce39, tierId, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            const string iosBaseUa = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148";
            const string fbWebViewHeaderUa = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 [FBAN/FBAV;IPHONE]";
            var ctx39 = CreateCheckoutContext(
                token39,
                nonce39,
                tierId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                userAgent: fbWebViewHeaderUa,
                clientUserAgent: iosBaseUa);
            middleware.InvokeAsync(ctx39, dummyGuard, networkService, tokenService, nonceService, uaValidator).GetAwaiter().GetResult();
            sw.Stop();
            var pass39 = ctx39.Response.StatusCode == StatusCodes.Status200OK
                         && ctx39.Request.Headers["X-AntiBot-UA-Consistent"] == "True";
            results.Add(new SelfTestResult(
                "TC-39: Middleware Flow - In-App WebView Normalization (FB/Zalo Trailing Tokens -> HTTP 200)",
                pass39,
                $"Status={ctx39.Response.StatusCode}, ConsistentHeader={ctx39.Request.Headers["X-AntiBot-UA-Consistent"]}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // =========================================================================
            // ML-13.3.1: DATA COLLECTION MODE TESTS
            // =========================================================================

            // TC-41: Data Collection Mode - Blatant OS Mismatch (HTTP 403 with X-AntiBot-Debug-Vector)
            sw.Restart();
            var (token41, nonce41, _, _) = tokenService.GenerateSessionTokenWithNonce(tierId, null, 300);
            nonceService.RegisterNonceAsync(nonce41, tierId, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            var ctx41 = CreateCheckoutContext(
                token41,
                nonce41,
                tierId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                userAgent: linuxHeaderUa,
                clientUserAgent: ChromeUa);
            ctx41.Request.Headers["X-Data-Collection-Mode"] = "True";
            middleware.InvokeAsync(ctx41, dummyGuard, networkService, tokenService, nonceService, uaValidator).GetAwaiter().GetResult();
            sw.Stop();
            var hasDebugVector41 = ctx41.Response.Headers.ContainsKey("X-AntiBot-Debug-Vector");
            var debugVectorStr41 = ctx41.Response.Headers["X-AntiBot-Debug-Vector"].ToString();
            var pass41 = ctx41.Response.StatusCode == StatusCodes.Status403Forbidden
                         && hasDebugVector41
                         && debugVectorStr41.Contains("\"ua_match_level\":\"BlatantMismatch\"");
            results.Add(new SelfTestResult(
                "TC-41: Data Collection Mode - Blatant OS Mismatch with Debug Vector Header",
                pass41,
                $"Status={ctx41.Response.StatusCode}, HasDebugHeader={hasDebugVector41}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-42: Data Collection Mode - Consistent Chrome Windows (HTTP 200 with X-AntiBot-Debug-Vector)
            sw.Restart();
            var (token42, nonce42, _, _) = tokenService.GenerateSessionTokenWithNonce(tierId, null, 300);
            nonceService.RegisterNonceAsync(nonce42, tierId, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            var ctx42 = CreateCheckoutContext(
                token42,
                nonce42,
                tierId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                userAgent: ChromeUa,
                clientUserAgent: ChromeUa);
            ctx42.Request.Headers["X-Data-Collection-Mode"] = "True";
            middleware.InvokeAsync(ctx42, dummyGuard, networkService, tokenService, nonceService, uaValidator).GetAwaiter().GetResult();
            sw.Stop();
            var hasDebugVector42 = ctx42.Response.Headers.ContainsKey("X-AntiBot-Debug-Vector");
            var debugVectorStr42 = ctx42.Response.Headers["X-AntiBot-Debug-Vector"].ToString();
            var pass42 = ctx42.Response.StatusCode == StatusCodes.Status200OK
                         && hasDebugVector42
                         && debugVectorStr42.Contains("\"ua_match_level\":\"ExactMatch\"");
            results.Add(new SelfTestResult(
                "TC-42: Data Collection Mode - Consistent Chrome Windows with Debug Vector Header",
                pass42,
                $"Status={ctx42.Response.StatusCode}, HasDebugHeader={hasDebugVector42}",
                sw.Elapsed.TotalMilliseconds * 1000.0));
        }

        // TC-40: Benchmark 50,000 UA Cross-Validations (SLA < 10µs)
        sw.Restart();
        const int uaBenchmarkIterations = 50_000;
        for (var i = 0; i < uaBenchmarkIterations; i++)
        {
            uaValidator.Validate(ChromeUa, ChromeUa);
        }
        sw.Stop();
        var avgUaBenchmarkUs = (sw.Elapsed.TotalMilliseconds * 1000.0) / uaBenchmarkIterations;
        var pass40 = avgUaBenchmarkUs < 10.0;
        results.Add(new SelfTestResult(
            "TC-40: Benchmark 50,000 UA Cross-Validations (SLA < 10µs)",
            pass40,
            $"Completed in {sw.ElapsedMilliseconds}ms, Avg = {avgUaBenchmarkUs:F4} µs/op",
            avgUaBenchmarkUs));

        // =========================================================================
        // ML-13.3.3 / ML-13.3.5: AI MODEL v2.0 & SHADOW MODE EVALUATION
        // =========================================================================
        double avgV2BenchmarkUs = 0.0;
        if (scoringService != null)
        {
            // TC-43: Model v2.0 ONNX Initialization & SHA-256 Hash Verification
            sw.Restart();
            var isReady43 = scoringService.IsModelV2Ready;
            var hash43 = scoringService.ModelV2Sha256;
            sw.Stop();
            var pass43 = isReady43 && !string.IsNullOrEmpty(hash43) && hash43 == "d647e87b5e23f3d6302ffb6ed597010388abc4e243a1c683f4293da2fa3f570c";
            results.Add(new SelfTestResult(
                "TC-43: Model v2.0 ONNX Initialization & SHA-256 Verification",
                pass43,
                $"IsReady={isReady43}, Sha256={hash43}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-44: Model v2.0 Human Profile Evaluation (11D -> Allow < 0.35)
            sw.Restart();
            var humanV2 = new BotRiskVectorV2
            {
                IsWebDriver = 0.0f,
                VirtualGpuDetected = 0.0f,
                PrototypeTampered = 0.0f,
                CanvasTampered = 0.0f,
                ActionIntervalStdDev = 150.0f,
                UaMatchLevel = 0.0f,
                NetworkInconsistencyScore = 0.0f,
                TimeToCheckout = 18.0f,
                BurstRate10s = 1.0f,
                ConcurrentHold = 0.0f,
                DeviceAccountOverlap = 0.0f
            };
            var resHuman44 = scoringService.EvaluateV2(humanV2);
            sw.Stop();
            var pass44 = resHuman44.Decision == "Allow" && resHuman44.RiskScore < 0.35f;
            results.Add(new SelfTestResult(
                "TC-44: Model v2.0 Human Profile Evaluation (11D -> Allow)",
                pass44,
                $"Decision={resHuman44.Decision}, Risk={resHuman44.RiskScore}, Latency={resHuman44.InferenceTimeMs}ms",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-45: Model v2.0 Brave Farbling Profile Evaluation (CanvasTampered=1.0 -> Allow < 0.35, Zero False Positive!)
            sw.Restart();
            var braveV2 = new BotRiskVectorV2
            {
                IsWebDriver = 0.0f,
                VirtualGpuDetected = 0.0f,
                PrototypeTampered = 0.0f,
                CanvasTampered = 1.0f, // Brave Farbling ngẫu hóa canvas
                ActionIntervalStdDev = 175.0f, // Nhịp bấm sinh học tự nhiên
                UaMatchLevel = 0.0f,
                NetworkInconsistencyScore = 0.0f,
                TimeToCheckout = 14.5f,
                BurstRate10s = 1.0f,
                ConcurrentHold = 0.0f,
                DeviceAccountOverlap = 0.0f
            };
            var resBrave45 = scoringService.EvaluateV2(braveV2);
            sw.Stop();
            var pass45 = resBrave45.Decision == "Allow" && resBrave45.RiskScore < 0.35f;
            results.Add(new SelfTestResult(
                "TC-45: Model v2.0 Brave Farbling Zero False-Positive (Allow < 0.35)",
                pass45,
                $"Decision={resBrave45.Decision}, Risk={resBrave45.RiskScore}, Latency={resBrave45.InferenceTimeMs}ms",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-46: Model v2.0 Adversarial Stealth Bot Evaluation (11D -> Block >= 0.75)
            sw.Restart();
            var stealthV2 = new BotRiskVectorV2
            {
                IsWebDriver = 1.0f,
                VirtualGpuDetected = 0.0f,
                PrototypeTampered = 1.0f,
                CanvasTampered = 0.0f,
                ActionIntervalStdDev = 5.0f,
                UaMatchLevel = 0.5f,
                NetworkInconsistencyScore = 0.4f,
                TimeToCheckout = 0.25f,
                BurstRate10s = 12.0f,
                ConcurrentHold = 5.0f,
                DeviceAccountOverlap = 0.8f
            };
            var resStealth46 = scoringService.EvaluateV2(stealthV2);
            sw.Stop();
            var pass46 = resStealth46.Decision == "Block" && resStealth46.RiskScore >= 0.75f;
            results.Add(new SelfTestResult(
                "TC-46: Model v2.0 Adversarial Stealth Bot Detection (Block >= 0.75)",
                pass46,
                $"Decision={resStealth46.Decision}, Risk={resStealth46.RiskScore}, Latency={resStealth46.InferenceTimeMs}ms",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-47: Shadow Mode Dual-Evaluation Cohort Verification
            sw.Restart();
            var v1_human = new BotRiskVector
            {
                TimeToCheckout = 18.0f,
                ActionIntervalStdDev = 150.0f,
                BurstRate10s = 1.0f,
                ConcurrentHold = 0.0f,
                IsWebDriver = 0.0f,
                DeviceAccountOverlap = 0.0f
            };
            var shadowRes47 = scoringService.EvaluateShadow(v1_human, humanV2);
            sw.Stop();
            var pass47 = shadowRes47.ShadowCohort == "v1_allow_v2_allow" && shadowRes47.DecisionMatches;
            results.Add(new SelfTestResult(
                "TC-47: Shadow Mode Dual-Evaluation Cohort Verification",
                pass47,
                $"Cohort={shadowRes47.ShadowCohort}, DecisionMatches={shadowRes47.DecisionMatches}, Delta={shadowRes47.RiskScoreDelta}",
                sw.Elapsed.TotalMilliseconds * 1000.0));

            // TC-48: Model v2.0 CPU Latency Benchmark 1,000 Inferences (SLA < 1.0ms)
            sw.Restart();
            const int v2BenchmarkIterations = 1_000;
            for (var i = 0; i < v2BenchmarkIterations; i++)
            {
                scoringService.EvaluateV2(humanV2);
            }
            sw.Stop();
            avgV2BenchmarkUs = (sw.Elapsed.TotalMilliseconds * 1000.0) / v2BenchmarkIterations;
            var pass48 = avgV2BenchmarkUs < 1000.0; // SLA < 1ms = 1,000µs
            results.Add(new SelfTestResult(
                "TC-48: Model v2.0 In-Process CPU Latency Benchmark 1,000 Inferences (SLA < 1.0ms)",
                pass48,
                $"Completed in {sw.ElapsedMilliseconds}ms, Avg = {avgV2BenchmarkUs:F2} µs/op",
                avgV2BenchmarkUs));
        }

        var allPassed = results.All(r => r.Passed);
        var passedCount = results.Count(r => r.Passed);

        var overallAvgBenchmarkUs = scoringService != null
            ? (avgH2BenchmarkUs + avgChBenchmarkUs + avgUaBenchmarkUs + avgV2BenchmarkUs) / 4.0
            : (avgH2BenchmarkUs + avgChBenchmarkUs + avgUaBenchmarkUs) / 3.0;

        return new SelfTestSuiteReport(
            allPassed,
            results.Count,
            passedCount,
            overallAvgBenchmarkUs,
            results);
    }

    private static DefaultHttpContext CreateCheckoutContext(
        string token,
        string nonce,
        Guid tierId,
        long clientTimestampMs,
        string? userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
        string? clientUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/organizer/orders/checkout";
        ctx.Request.ContentType = "application/json";
        ctx.Response.Body = new MemoryStream();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        if (userAgent != null)
        {
            ctx.Request.Headers.UserAgent = userAgent;
        }

        var bodyObj = new
        {
            sessionToken = token,
            tierId = tierId,
            buyerEmail = "buyer@ticketshield.io",
            telemetry = new
            {
                is_webdriver = 0,
                virtual_gpu_detected = 0,
                prototype_tampered = 0,
                device_fingerprint = "0123456789abcdef0123456789abcdef",
                nonce = nonce,
                client_timestamp = clientTimestampMs,
                client_user_agent = clientUserAgent
            }
        };

        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(bodyObj));
        ctx.Request.Body = new MemoryStream(jsonBytes);
        ctx.Request.ContentLength = jsonBytes.Length;

        return ctx;
    }

    private class DummySelfTestGuardService : IAntiBotGuardService
    {
        public Task<AntiBotGuardResult> EvaluateCheckoutGuardAsync(
            CheckoutTelemetryContext context,
            CancellationToken ct = default)
        {
            return EvaluateCheckoutGuardAsync(
                context.SessionToken,
                context.BuyerEmail,
                context.ClientIp,
                context.DeviceFingerprint,
                context.ActionIntervalStdDev,
                context.IsWebDriver,
                ct);
        }

        public Task<AntiBotGuardResult> EvaluateCheckoutGuardAsync(
            string? sessionToken, string? buyerEmail, string? clientIp,
            string? deviceFingerprint, float? actionIntervalStdDev, float? isWebdriver,
            CancellationToken ct = default)
        {
            return Task.FromResult(new AntiBotGuardResult
            {
                RiskScore = 0.1f,
                Decision = AntiBotDecision.Allow,
                ExecutionDurationMs = 1.0,
                IsFailOpen = false,
                Reason = "SelfTest Mock Allow",
                Vector = new BotRiskVector
                {
                    IsWebDriver = isWebdriver ?? -1.0f,
                    ActionIntervalStdDev = actionIntervalStdDev ?? -1.0f
                }
            });
        }
    }
}
