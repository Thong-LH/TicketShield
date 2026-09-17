using Microsoft.Extensions.Options;
using TicketShield.Application.Common.Configurations;
using TicketShield.Application.Common.Models;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

public class VietQrServiceTests
{
    private readonly IOptions<VietQrSettings> _options;

    public VietQrServiceTests()
    {
        var settings = new VietQrSettings
        {
            Provider = "SePAY",
            BaseUrl = "https://my.sepay.vn/userapi",
            ApiKey = "test_key",
            WebhookSecret = "test_secret",
            BankBin = "970422",
            AccountNumber = "0938434102",
            AccountName = "NGUYEN HUNG THINH",
            QrTemplate = "compact2"
        };
        _options = Options.Create(settings);
    }

    [Fact]
    public void GenerateSystemQuickLink_ShouldReturnValidVietQrImageUrlAndMetadata()
    {
        // Arrange
        var service = new VietQrService(_options);
        decimal amount = 1050000m;
        string transferContent = "TS8F3K9P2A";

        // Act
        var result = service.GenerateSystemQuickLink(amount, transferContent);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("970422", result.BankBin);
        Assert.Equal("0938434102", result.AccountNumber);
        Assert.Equal("NGUYEN HUNG THINH", result.AccountName);
        Assert.Equal(1050000m, result.Amount);
        Assert.Equal("TS8F3K9P2A", result.TransferContent);
        Assert.StartsWith("https://img.vietqr.io/image/970422-0938434102-compact2.png", result.QrImageUrl);
        Assert.Contains("amount=1050000", result.QrImageUrl);
        Assert.Contains("addInfo=TS8F3K9P2A", result.QrImageUrl);
        Assert.Contains("accountName=NGUYEN+HUNG+THINH", result.QrImageUrl);
    }

    [Fact]
    public void GenerateQuickLink_WithCustomBankInfo_ShouldUseCustomValues()
    {
        // Arrange
        var service = new VietQrService(_options);
        var request = new VietQrQuickLinkRequest
        {
            BankBin = "970415", // VietinBank
            AccountNumber = "10987654321",
            AccountName = "LE VAN SELLER",
            Amount = 500000m,
            TransferContent = "TS999999",
            Template = "compact"
        };

        // Act
        var result = service.GenerateQuickLink(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("970415", result.BankBin);
        Assert.Equal("10987654321", result.AccountNumber);
        Assert.Equal("LE VAN SELLER", result.AccountName);
        Assert.Equal(500000m, result.Amount);
        Assert.StartsWith("https://img.vietqr.io/image/970415-10987654321-compact.png", result.QrImageUrl);
        Assert.Contains("amount=500000", result.QrImageUrl);
        Assert.Contains("addInfo=TS999999", result.QrImageUrl);
        Assert.Contains("accountName=LE+VAN+SELLER", result.QrImageUrl);
    }

    [Fact]
    public void GenerateQuickLink_WithVietnameseAccentNames_ShouldUrlEncodeCorrectly()
    {
        // Arrange
        var service = new VietQrService(_options);
        var request = new VietQrQuickLinkRequest
        {
            BankBin = "970422",
            AccountNumber = "0938434102",
            AccountName = "NGUYỄN HÙNG THỊNH",
            Amount = 2500000m,
            TransferContent = "TS SPECIAL CODE"
        };

        // Act
        var result = service.GenerateQuickLink(request);

        // Assert
        Assert.NotNull(result);
        Assert.DoesNotContain(" ", result.QrImageUrl); // No unencoded space
        Assert.Contains("addInfo=TS+SPECIAL+CODE", result.QrImageUrl);
    }
}
