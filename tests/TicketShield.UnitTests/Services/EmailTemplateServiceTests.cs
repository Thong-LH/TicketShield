using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

public class EmailTemplateServiceTests
{
    [Fact]
    public void GetBuyerTicketIssuedEmailHtml_ShouldLoadTemplateAndReplaceTokens()
    {
        // Arrange
        var service = new EmailTemplateService();

        // Act
        var html = service.GetBuyerTicketIssuedEmailHtml(
            buyerName: "Nguyen Van A",
            eventName: "Concert 2026",
            eventDate: "2026-10-10 19:00",
            venue: "My Dinh Stadium",
            ticketTier: "VIP 1",
            seatNumber: "A-12",
            ticketCode: "TCK-123456",
            qrCodeDataUrl: "data:image/png;base64,mockqr",
            escrowCode: "TS12345678",
            amountPaid: 1500000m);

        // Assert
        Assert.NotNull(html);
        Assert.Contains("Nguyen Van A", html);
        Assert.Contains("Concert 2026", html);
        Assert.Contains("VIP 1", html);
        Assert.Contains("TCK-123456", html);
        Assert.Contains("TS12345678", html);
        Assert.Contains("1,500,000", html);
    }

    [Fact]
    public void GetSellerEscrowLockedEmailHtml_ShouldLoadTemplateAndReplaceTokens()
    {
        // Arrange
        var service = new EmailTemplateService();

        // Act
        var html = service.GetSellerEscrowLockedEmailHtml(
            sellerName: "Tran Van B",
            buyerName: "Nguyen Van A",
            listingTitle: "Ve VIP Concert 2026",
            eventName: "Concert 2026",
            amountLocked: 1400000m,
            escrowCode: "TS12345678");

        // Assert
        Assert.NotNull(html);
        Assert.Contains("Tran Van B", html);
        Assert.Contains("Nguyen Van A", html);
        Assert.Contains("Ve VIP Concert 2026", html);
        Assert.Contains("Concert 2026", html);
        Assert.Contains("1,400,000", html);
        Assert.Contains("TS12345678", html);
    }

    [Fact]
    public void GetSellerPayoutStatementEmailHtml_ShouldLoadTemplateAndReplaceTokens()
    {
        // Arrange
        var service = new EmailTemplateService();

        // Act
        var html = service.GetSellerPayoutStatementEmailHtml(
            sellerName: "Tran Duc Linh",
            payoutCode: "PO-ABCD1234",
            bankReference: "FT262788192837",
            escrowCode: "TSREF-98765432",
            eventName: "Anh Trai Say Hi",
            tierName: "VIP Zone A",
            ticketCode: "ATSH-VIP-888",
            grossPrice: 2500000m,
            sellerFee: 75000m,
            netPayout: 2425000m,
            bankCode: "MB",
            accountNumber: "0329952127",
            accountName: "TRAN DUC LINH",
            processedAt: "05/10/2026 10:15:30");

        // Assert
        Assert.NotNull(html);
        Assert.Contains("Tran Duc Linh", html);
        Assert.Contains("PO-ABCD1234", html);
        Assert.Contains("FT262788192837", html);
        Assert.Contains("TSREF-98765432", html);
        Assert.Contains("Anh Trai Say Hi", html);
        Assert.Contains("VIP Zone A", html);
        Assert.Contains("ATSH-VIP-888", html);
        Assert.Contains("2,500,000", html);
        Assert.Contains("75,000", html);
        Assert.Contains("2,425,000", html);
        Assert.Contains("MB", html);
        Assert.Contains("0329952127", html);
        Assert.Contains("TRAN DUC LINH", html);
    }
}
