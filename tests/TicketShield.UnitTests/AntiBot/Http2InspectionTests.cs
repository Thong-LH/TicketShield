using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.AntiBot.Gateway.Models.Network;
using TicketShield.AntiBot.Gateway.Services.Network;

namespace TicketShield.UnitTests.AntiBot;

public class Http2InspectionTests
{
    private readonly PseudoHeaderInspector _pseudoHeaderInspector = new(NullLogger<PseudoHeaderInspector>.Instance);
    private readonly Http2SettingsInspector _settingsInspector = new(NullLogger<Http2SettingsInspector>.Instance);
    private readonly Http2ConsistencyService _consistencyService;

    public const string ChromeUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    public const string FirefoxUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:130.0) Gecko/20100101 Firefox/130.0";
    public const string PythonBotUserAgent = "python-requests/2.31.0";

    public Http2InspectionTests()
    {
        _consistencyService = new Http2ConsistencyService(
            _pseudoHeaderInspector,
            _settingsInspector,
            NullLogger<Http2ConsistencyService>.Instance);
    }

    [Fact]
    public void Inspect_ChromeConsistent_ReturnsScoreZeroAndConsistent()
    {
        // Arrange: Chrome chuẩn: m,a,s,p + Window size 6291456 (6MB)
        var context = new DefaultHttpContext();
        context.Request.Protocol = "HTTP/2";
        context.Request.Headers["X-Http2-Pseudo-Headers"] = "m,a,s,p";
        context.Request.Headers["X-Http2-Settings"] = "1:65536;2:0;4:6291456;6:262144";

        // Act
        var profile = _consistencyService.Inspect(context, ChromeUserAgent);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(Http2InspectionStatus.Consistent, profile.Status);
        Assert.Equal(0.0f, profile.NetworkInconsistencyScore);
        Assert.Null(profile.AnomalyReason);
        Assert.Equal("m,a,s,p", profile.PseudoHeaderOrder);
        Assert.NotNull(profile.Settings);
        Assert.Equal(6291456u, profile.Settings.InitialWindowSize);
    }

    [Fact]
    public void Inspect_ChromeUaWithFirefoxPseudoHeaderOrder_FlagsInconsistencyScoreOne()
    {
        // Arrange: Bot giả mạo Chrome nhưng gửi thứ tự pseudo-headers của Firefox (m,p,a,s)
        var context = new DefaultHttpContext();
        context.Request.Protocol = "HTTP/2";
        context.Request.Headers["X-Http2-Pseudo-Headers"] = "m,p,a,s"; // Firefox signature
        context.Request.Headers["X-Http2-Settings"] = "1:65536;2:0;4:6291456;6:262144";

        // Act
        var profile = _consistencyService.Inspect(context, ChromeUserAgent);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(Http2InspectionStatus.Inconsistent, profile.Status);
        Assert.Equal(1.0f, profile.NetworkInconsistencyScore);
        Assert.NotNull(profile.AnomalyReason);
        Assert.Contains("Firefox", profile.AnomalyReason);
    }

    [Fact]
    public void Inspect_ChromeUaWithPythonRfcDefaultWindowSize_FlagsInconsistencyScoreOne()
    {
        // Arrange: Bot giả mạo Chrome nhưng gửi SETTINGS InitialWindowSize = 65535 (RFC 7540 default)
        var context = new DefaultHttpContext();
        context.Request.Protocol = "HTTP/2";
        context.Request.Headers["X-Http2-Pseudo-Headers"] = "m,a,s,p";
        context.Request.Headers["X-Http2-Settings"] = "1:65536;2:0;4:65535;6:262144"; // 65535 = Python bot

        // Act
        var profile = _consistencyService.Inspect(context, ChromeUserAgent);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(Http2InspectionStatus.Inconsistent, profile.Status);
        Assert.Equal(1.0f, profile.NetworkInconsistencyScore);
        Assert.NotNull(profile.AnomalyReason);
        Assert.Contains("RFC 7540 default", profile.AnomalyReason);
    }

    [Fact]
    public void Inspect_LocalhostPlainHttp11_AppliesGracePeriodAndReturnsZeroScore()
    {
        // Arrange: Request HTTP/1.1 thông thường tại localhost dev không có proxy header
        var context = new DefaultHttpContext();
        context.Request.Protocol = "HTTP/1.1";
        context.Request.Scheme = "http";

        // Act
        var profile = _consistencyService.Inspect(context, ChromeUserAgent);

        // Assert: Không phạt nhầm môi trường dev
        Assert.NotNull(profile);
        Assert.Equal(Http2InspectionStatus.NotApplicable, profile.Status);
        Assert.Equal(0.0f, profile.NetworkInconsistencyScore);
        Assert.False(profile.IsHttp2Connection);
    }

    [Fact]
    public void Inspect_ProtocolDowngradeAttack_FlagsInconsistencyScoreOne()
    {
        // Arrange: TLS ALPN đã đàm phán h2 nhưng Application client gửi HTTP/1.1 để trốn kiểm tra
        var context = new DefaultHttpContext();
        context.Request.Protocol = "HTTP/1.1";
        context.Items["TlsProfile"] = new TlsProfile
        {
            IsSecureConnection = true,
            Alpn = "h2",
            TlsVersion = "Tls13"
        };

        // Act
        var profile = _consistencyService.Inspect(context, ChromeUserAgent);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(Http2InspectionStatus.Inconsistent, profile.Status);
        Assert.Equal(1.0f, profile.NetworkInconsistencyScore);
        Assert.NotNull(profile.AnomalyReason);
        Assert.Contains("Protocol Downgrade detected", profile.AnomalyReason);
    }

    [Fact]
    public void Inspect_AutomatedBotLibraryUserAgent_FlagsInconsistencyScoreOne()
    {
        // Arrange: User-Agent khai báo thẳng python-requests
        var context = new DefaultHttpContext();
        context.Request.Protocol = "HTTP/2";
        context.Request.Headers["X-Http2-Pseudo-Headers"] = "m,a,s,p";

        // Act
        var profile = _consistencyService.Inspect(context, PythonBotUserAgent);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(Http2InspectionStatus.Inconsistent, profile.Status);
        Assert.Equal(1.0f, profile.NetworkInconsistencyScore);
        Assert.NotNull(profile.AnomalyReason);
        Assert.Contains("Automated HTTP client library", profile.AnomalyReason);
    }

    [Fact]
    public void Inspect_AkamaiCombinedFingerprint_ParsesSettingsAndHeadersCorrectly()
    {
        // Arrange: Chuỗi Akamai kết hợp: settings|window|priority|headers
        var context = new DefaultHttpContext();
        context.Request.Protocol = "HTTP/2";
        context.Request.Headers["X-Akamai-Http2-Fingerprint"] = "1:65536;2:0;4:6291456;6:262144|15663105|0|m,a,s,p";

        // Act
        var profile = _consistencyService.Inspect(context, ChromeUserAgent);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(Http2InspectionStatus.Consistent, profile.Status);
        Assert.Equal(0.0f, profile.NetworkInconsistencyScore);
        Assert.Equal("m,a,s,p", profile.PseudoHeaderOrder);
        Assert.NotNull(profile.Settings);
        Assert.Equal(6291456u, profile.Settings.InitialWindowSize);
        Assert.Equal(0u, profile.Settings.EnablePush);
    }
}
