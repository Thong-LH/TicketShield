using System.Net.Security;
using System.Security.Authentication;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TicketShield.AntiBot.Gateway.Models.Network;
using TicketShield.AntiBot.Gateway.Services.Network;

namespace TicketShield.UnitTests.AntiBot;

public class TlsProfileExtractorTests
{
    private readonly Ja4Parser _ja4Parser = new();
    private readonly TlsProfileExtractor _extractor;

    public TlsProfileExtractorTests()
    {
        _extractor = new TlsProfileExtractor(_ja4Parser, NullLogger<TlsProfileExtractor>.Instance);
    }

    [Fact]
    public void Extract_WithProxyHeaderXJa4_ReturnsProxyHeaderJa4Profile()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["X-JA4"] = "t13d1516h2_8daaf6152771_02711d0e519e";

        // Act
        var profile = _extractor.Extract(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(TlsSourceType.ProxyHeaderJa4, profile.Source);
        Assert.True(profile.IsSecureConnection);
        Assert.Equal("Tls13", profile.TlsVersion);
        Assert.Equal("h2", profile.Alpn);
        Assert.NotNull(profile.Ja4);
        Assert.True(profile.Ja4.IsValid);
        Assert.Equal(15, profile.Ja4.CipherCount);
        Assert.Equal("8daaf6152771", profile.Ja4.CipherHash);
    }

    [Fact]
    public void Extract_WithCfJa4Header_ReturnsProxyHeaderJa4Profile()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["CF-JA4"] = "t12i0508h1_c02bc02f009e_000000000000";

        // Act
        var profile = _extractor.Extract(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(TlsSourceType.ProxyHeaderJa4, profile.Source);
        Assert.True(profile.IsSecureConnection);
        Assert.Equal("Tls12", profile.TlsVersion);
        Assert.Equal("http/1.1", profile.Alpn);
        Assert.NotNull(profile.Ja4);
        Assert.Equal('i', profile.Ja4.SniType);
    }

    [Fact]
    public void Extract_WithKestrelHandshakeFeature_ReturnsKestrelDirectProfile()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Protocol = "HTTP/2";

        var handshakeMock = new Mock<ITlsHandshakeFeature>();
        handshakeMock.SetupGet(h => h.Protocol).Returns(SslProtocols.Tls13);
        handshakeMock.SetupGet(h => h.NegotiatedCipherSuite).Returns(TlsCipherSuite.TLS_AES_128_GCM_SHA256);

        context.Features.Set(handshakeMock.Object);

        // Act
        var profile = _extractor.Extract(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(TlsSourceType.KestrelDirect, profile.Source);
        Assert.True(profile.IsSecureConnection);
        Assert.Equal("Tls13", profile.TlsVersion);
        Assert.Equal("TLS_AES_128_GCM_SHA256", profile.NegotiatedCipherSuite);
        Assert.Equal("h2", profile.Alpn);
        Assert.Null(profile.Ja4);
    }

    [Fact]
    public void Extract_WithNonTlsHttpRequest_ReturnsNonTlsProfile()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Protocol = "HTTP/1.1";

        // Act
        var profile = _extractor.Extract(context);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(TlsSourceType.None, profile.Source);
        Assert.False(profile.IsSecureConnection);
        Assert.Null(profile.TlsVersion);
        Assert.Null(profile.NegotiatedCipherSuite);
        Assert.Null(profile.Alpn);
    }

    [Fact]
    public void Extract_WithMalformedProxyHeader_FallsBackToKestrelOrNonTls()
    {
        // Arrange: Header X-JA4 sai định dạng
        var context = new DefaultHttpContext();
        context.Request.Headers["X-JA4"] = "malformed_ja4_value";
        context.Request.Scheme = "http";

        // Act
        var profile = _extractor.Extract(context);

        // Assert: Không crash, tự động fallback an toàn
        Assert.NotNull(profile);
        Assert.Equal(TlsSourceType.None, profile.Source);
        Assert.False(profile.IsSecureConnection);
    }
}
