using TicketShield.AntiBot.Gateway.Models.Network;
using TicketShield.AntiBot.Gateway.Services.Network;

namespace TicketShield.UnitTests.AntiBot;

public class Ja4ParserTests
{
    private readonly Ja4Parser _parser = new();

    [Fact]
    public void TryParse_ValidChrome130Ja4_ReturnsTrueAndCorrectFields()
    {
        // Arrange: Chuẩn vân tay Chrome 130 trên Windows
        const string rawJa4 = "t13d1516h2_8daaf6152771_02711d0e519e";

        // Act
        var result = _parser.TryParse(rawJa4, out var parsed);

        // Assert
        Assert.True(result);
        Assert.NotNull(parsed);
        Assert.True(parsed.IsValid);
        Assert.Equal("t13d1516h2_8daaf6152771_02711d0e519e", parsed.RawJa4);
        Assert.Equal('t', parsed.Protocol);
        Assert.Equal("13", parsed.TlsVersion);
        Assert.Equal('d', parsed.SniType);
        Assert.Equal(15, parsed.CipherCount);
        Assert.Equal(16, parsed.ExtensionCount);
        Assert.Equal("h2", parsed.Alpn);
        Assert.Equal("8daaf6152771", parsed.CipherHash);
        Assert.Equal("02711d0e519e", parsed.ExtensionHash);
    }

    [Fact]
    public void TryParse_ValidQuicHttp3Ja4_ParsesCorrectly()
    {
        // Arrange: Chuẩn vân tay QUIC / HTTP3
        const string rawJa4 = "q13d1012h3_aabbccddeeff_112233445566";

        // Act
        var result = _parser.TryParse(rawJa4, out var parsed);

        // Assert
        Assert.True(result);
        Assert.NotNull(parsed);
        Assert.Equal('q', parsed.Protocol);
        Assert.Equal("13", parsed.TlsVersion);
        Assert.Equal('d', parsed.SniType);
        Assert.Equal(10, parsed.CipherCount);
        Assert.Equal(12, parsed.ExtensionCount);
        Assert.Equal("h3", parsed.Alpn);
        Assert.Equal("aabbccddeeff", parsed.CipherHash);
        Assert.Equal("112233445566", parsed.ExtensionHash);
    }

    [Fact]
    public void TryParse_ValidHttp11WithIpSni_ParsesCorrectly()
    {
        // Arrange: TLS 1.2, IP SNI ('i'), ALPN h1
        const string rawJa4 = "t12i0508h1_c02bc02f009e_000000000000";

        // Act
        var result = _parser.TryParse(rawJa4, out var parsed);

        // Assert
        Assert.True(result);
        Assert.NotNull(parsed);
        Assert.Equal('t', parsed.Protocol);
        Assert.Equal("12", parsed.TlsVersion);
        Assert.Equal('i', parsed.SniType);
        Assert.Equal(5, parsed.CipherCount);
        Assert.Equal(8, parsed.ExtensionCount);
        Assert.Equal("h1", parsed.Alpn);
        Assert.Equal("c02bc02f009e", parsed.CipherHash);
    }

    [Fact]
    public void TryParse_WithLeadingOrTrailingWhitespace_TrimsAndSucceeds()
    {
        // Arrange
        const string rawJa4 = "   t13d1516h2_8daaf6152771_02711d0e519e   ";

        // Act
        var result = _parser.TryParse(rawJa4, out var parsed);

        // Assert
        Assert.True(result);
        Assert.Equal("t13d1516h2_8daaf6152771_02711d0e519e", parsed.RawJa4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("t13d1516h2_too_short")]
    [InlineData("t13d1516h2_8daaf6152771_02711d0e519e_extra")]
    [InlineData("x13d1516h2_8daaf6152771_02711d0e519e")] // Sai protocol ('x')
    [InlineData("t13x1516h2_8daaf6152771_02711d0e519e")] // Sai SNI ('x')
    [InlineData("t13da516h2_8daaf6152771_02711d0e519e")] // Cipher count không phải số ('a5')
    [InlineData("t13d15b6h2_8daaf6152771_02711d0e519e")] // Ext count không phải số ('b6')
    [InlineData("t13d1516h2-8daaf6152771-02711d0e519e")] // Sai dấu gạch dưới ngăn cách
    [InlineData("t13d1516h2_8daaf615277z_02711d0e519e")] // Cipher hash chứa ký tự không phải hex ('z')
    [InlineData("t13d1516h2_8daaf6152771_02711d0e519g")] // Ext hash chứa ký tự không phải hex ('g')
    public void TryParse_InvalidFormat_ReturnsFalseAndEmpty(string? invalidJa4)
    {
        // Act
        var result = _parser.TryParse(invalidJa4, out var parsed);

        // Assert
        Assert.False(result);
        Assert.NotNull(parsed);
        Assert.False(parsed.IsValid);
        Assert.Equal(ParsedJa4.Empty, parsed);
    }

    [Fact]
    public void ParseOrDefault_ValidString_ReturnsParsedObject()
    {
        // Arrange
        const string rawJa4 = "t13d1516h2_8daaf6152771_02711d0e519e";

        // Act
        var parsed = _parser.ParseOrDefault(rawJa4);

        // Assert
        Assert.True(parsed.IsValid);
        Assert.Equal("13", parsed.TlsVersion);
        Assert.Equal("h2", parsed.Alpn);
    }

    [Fact]
    public void ParseOrDefault_InvalidString_ReturnsEmpty()
    {
        // Act
        var parsed = _parser.ParseOrDefault("invalid_ja4_string");

        // Assert
        Assert.False(parsed.IsValid);
        Assert.Equal(ParsedJa4.Empty, parsed);
    }
}
