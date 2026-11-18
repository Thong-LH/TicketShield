using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.AntiBot.Gateway.Models.Network;
using TicketShield.AntiBot.Gateway.Services.Network;

namespace TicketShield.UnitTests.AntiBot;

public class ClientHintsAndSubnetTests
{
    private readonly ClientHintsValidator _validator = new();
    private readonly SubnetCorrelationTracker _subnetTracker = new(
        redis: null,
        config: null,
        clock: null,
        logger: NullLogger<SubnetCorrelationTracker>.Instance);

    public const string ChromeDesktopUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    public const string SafariMacUa = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15";
    public const string FirefoxWindowsUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0";
    public const string MobileAppUa = "TicketShield-Mobile/2.1.0 (Android 14; Pixel 8)";

    [Fact]
    public void Validate_ChromeConsistent_ReturnsScoreZeroAndConsistent()
    {
        // Arrange: Chrome 130 gửi đủ Client Hints và Sec-Fetch chuẩn
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = ChromeDesktopUa;
        context.Request.Headers["Sec-Ch-Ua"] = "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\", \"Not?A_Brand\";v=\"99\"";
        context.Request.Headers["Sec-Ch-Ua-Mobile"] = "?0";
        context.Request.Headers["Sec-Ch-Ua-Platform"] = "\"Windows\"";
        context.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        context.Request.Headers["Sec-Fetch-Mode"] = "cors";
        context.Request.Headers["Sec-Fetch-Dest"] = "empty";

        // Act
        var profile = _validator.Validate(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(ClientHintsStatus.Consistent, profile.Status);
        Assert.Equal(0.0f, profile.InconsistencyScore);
        Assert.Null(profile.AnomalyReason);
        Assert.Equal(3, profile.ExtractedBrands.Count);
        Assert.Equal("?0", profile.SecChUaMobile);
        Assert.Equal("\"Windows\"", profile.SecChUaPlatform);
    }

    [Fact]
    public void Validate_SafariBrowser_ReturnsScoreZeroAndNotApplicable()
    {
        // Arrange: Safari thật không có Sec-Ch-Ua -> KHÔNG được phạt điểm false positive
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = SafariMacUa;
        context.Request.Headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

        // Act
        var profile = _validator.Validate(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(ClientHintsStatus.NotApplicable, profile.Status);
        Assert.Equal(0.0f, profile.InconsistencyScore);
        Assert.Null(profile.AnomalyReason);
    }

    [Fact]
    public void Validate_FirefoxBrowser_ReturnsScoreZeroAndNotApplicable()
    {
        // Arrange: Firefox thật không có Sec-Ch-Ua
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = FirefoxWindowsUa;

        // Act
        var profile = _validator.Validate(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(ClientHintsStatus.NotApplicable, profile.Status);
        Assert.Equal(0.0f, profile.InconsistencyScore);
    }

    [Fact]
    public void Validate_TicketShieldMobileApp_ReturnsScoreZeroAndNotApplicable()
    {
        // Arrange: App di động nội bộ
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = MobileAppUa;

        // Act
        var profile = _validator.Validate(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(ClientHintsStatus.NotApplicable, profile.Status);
        Assert.Equal(0.0f, profile.InconsistencyScore);
    }

    [Fact]
    public void Validate_BotCurlCffiMissingHints_FlagsScoreOneAndMissingHints()
    {
        // Arrange: Bot curl_cffi giả lập Chrome 130 nhưng thiếu hoàn toàn Sec-Ch-Ua
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = ChromeDesktopUa;
        // Không gửi Sec-Ch-Ua

        // Act
        var profile = _validator.Validate(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(ClientHintsStatus.MissingHints, profile.Status);
        Assert.Equal(1.0f, profile.InconsistencyScore);
        Assert.Contains("Sec-Ch-Ua header is missing entirely", profile.AnomalyReason);
    }

    [Fact]
    public void Validate_BotVersionMismatch_FlagsScoreOneAndVersionMismatch()
    {
        // Arrange: UA là Chrome 130 nhưng Sec-Ch-Ua lại gửi version 120
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = ChromeDesktopUa;
        context.Request.Headers["Sec-Ch-Ua"] = "\"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"";
        context.Request.Headers["Sec-Ch-Ua-Mobile"] = "?0";
        context.Request.Headers["Sec-Ch-Ua-Platform"] = "\"Windows\"";
        context.Request.Headers["Sec-Fetch-Site"] = "same-origin";

        // Act
        var profile = _validator.Validate(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(ClientHintsStatus.VersionMismatch, profile.Status);
        Assert.Equal(1.0f, profile.InconsistencyScore);
        Assert.Contains("Version skew detected", profile.AnomalyReason);
    }

    [Fact]
    public void Validate_BotPlatformMismatch_FlagsScoreOneAndPlatformMismatch()
    {
        // Arrange: UA là Windows nhưng Sec-Ch-Ua-Platform lại nói Linux
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = ChromeDesktopUa;
        context.Request.Headers["Sec-Ch-Ua"] = "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\"";
        context.Request.Headers["Sec-Ch-Ua-Platform"] = "\"Linux\"";
        context.Request.Headers["Sec-Fetch-Site"] = "same-origin";

        // Act
        var profile = _validator.Validate(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(ClientHintsStatus.PlatformMismatch, profile.Status);
        Assert.Equal(1.0f, profile.InconsistencyScore);
        Assert.Contains("Platform mismatch", profile.AnomalyReason);
    }

    [Fact]
    public void Validate_SafariSpooferSendingChromiumHints_FlagsScoreOneAndBrandMismatch()
    {
        // Arrange: UA nói là Safari nhưng lại gửi kèm header Sec-Ch-Ua của Chromium
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = SafariMacUa;
        context.Request.Headers["Sec-Ch-Ua"] = "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\"";

        // Act
        var profile = _validator.Validate(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(ClientHintsStatus.BrandMismatch, profile.Status);
        Assert.Equal(1.0f, profile.InconsistencyScore);
        Assert.Contains("unexpectedly sent Chromium Client Hints", profile.AnomalyReason);
    }

    [Fact]
    public void Validate_ModernChromeMissingSecFetch_FlagsScoreOneAndSecFetchMismatch()
    {
        // Arrange: Chrome 130 có Sec-Ch-Ua nhưng thiếu toàn bộ Sec-Fetch-*
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = ChromeDesktopUa;
        context.Request.Headers["Sec-Ch-Ua"] = "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\"";
        context.Request.Headers["Sec-Ch-Ua-Mobile"] = "?0";
        context.Request.Headers["Sec-Ch-Ua-Platform"] = "\"Windows\"";
        // Thiếu Sec-Fetch-Site, Mode, Dest

        // Act
        var profile = _validator.Validate(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(ClientHintsStatus.SecFetchMismatch, profile.Status);
        Assert.Equal(1.0f, profile.InconsistencyScore);
        Assert.Contains("Missing all Sec-Fetch-* headers", profile.AnomalyReason);
    }

    [Fact]
    public async Task TrackSubnetAsync_LocalhostAndPrivateIp_IsExemptFromRateLimiting()
    {
        // Arrange: Spam 50 requests từ localhost (127.0.0.1)
        for (int i = 0; i < 50; i++)
        {
            var result = await _subnetTracker.TrackSubnetAsync("127.0.0.1");
            Assert.True(result.IsExempt);
            Assert.False(result.IsAnomaly);
            Assert.Equal(0.0f, result.InconsistencyScore);
        }

        // Test Private IP (192.168.1.100)
        var privateResult = await _subnetTracker.TrackSubnetAsync("192.168.1.100");
        Assert.True(privateResult.IsExempt);
        Assert.False(privateResult.IsAnomaly);
    }

    [Fact]
    public async Task TrackSubnetAsync_ResidentialProxyClusterAttack_FlagsScoreOneAndAnomaly()
    {
        // Arrange: Cụm bot xoay vòng IP trên cùng dải 198.51.100.0/24 (16 requests từ 8 IP khác nhau)
        SubnetRateMetrics? lastMetric = null;
        for (int i = 1; i <= 16; i++)
        {
            var ip = $"198.51.100.{i % 8 + 1}";
            lastMetric = await _subnetTracker.TrackSubnetAsync(ip);
        }

        // Assert
        Assert.NotNull(lastMetric);
        Assert.False(lastMetric.IsExempt);
        Assert.Equal("198.51.100.0/24", lastMetric.SubnetCidr);
        Assert.True(lastMetric.IsAnomaly);
        Assert.Equal(1.0f, lastMetric.InconsistencyScore);
        Assert.Contains("Residential proxy botnet detected", lastMetric.AnomalyReason);
    }

    [Fact]
    public async Task NetworkConsistencyService_EndToEndEvaluation_AggregatesCorrectly()
    {
        // Arrange
        var parser = new Ja4Parser();
        var tlsExtractor = new TlsProfileExtractor(parser, NullLogger<TlsProfileExtractor>.Instance);
        var pseudoInspector = new PseudoHeaderInspector(NullLogger<PseudoHeaderInspector>.Instance);
        var settingsInspector = new Http2SettingsInspector(NullLogger<Http2SettingsInspector>.Instance);
        var http2Service = new Http2ConsistencyService(pseudoInspector, settingsInspector, NullLogger<Http2ConsistencyService>.Instance);

        var facade = new NetworkConsistencyService(
            tlsExtractor,
            http2Service,
            _validator,
            _subnetTracker,
            NullLogger<NetworkConsistencyService>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = ChromeDesktopUa;
        context.Request.Headers["Sec-Ch-Ua"] = "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\"";
        context.Request.Headers["Sec-Ch-Ua-Mobile"] = "?0";
        context.Request.Headers["Sec-Ch-Ua-Platform"] = "\"Windows\"";
        context.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        context.Request.Headers["Sec-Fetch-Mode"] = "cors";
        context.Request.Headers["Sec-Fetch-Dest"] = "empty";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

        // Act
        var summary = await facade.EvaluateAsync(context);

        // Assert
        Assert.NotNull(summary);
        Assert.False(summary.HasInconsistency);
        Assert.Equal(0.0f, summary.OverallInconsistencyScore);
        Assert.True(summary.Subnet.IsExempt);
        Assert.Equal(ClientHintsStatus.Consistent, summary.ClientHints.Status);
    }
}
