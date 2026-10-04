using Microsoft.EntityFrameworkCore;
using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

public class PayoutReportApplierTests
{
    private readonly TicketShieldDbContext _dbContext;
    private readonly Mock<IPayoutRealtimeNotifier> _mockNotifier;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<IEmailTemplateService> _mockEmailTemplates;
    private readonly PayoutReportApplier _sut;

    public PayoutReportApplierTests()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: $"PayoutReportApplierTests_{Guid.NewGuid()}")
            .Options;
        _dbContext = new TicketShieldDbContext(options);

        _mockNotifier = new Mock<IPayoutRealtimeNotifier>();
        _mockEmailService = new Mock<IEmailService>();
        _mockEmailTemplates = new Mock<IEmailTemplateService>();

        _mockEmailTemplates.Setup(t => t.GetSellerPayoutStatementEmailHtml(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<decimal>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Returns("<html><body>Mock Payout Statement Email</body></html>");

        _sut = new PayoutReportApplier(
            _dbContext,
            _mockNotifier.Object,
            _mockEmailService.Object,
            _mockEmailTemplates.Object);
    }

    [Fact]
    public async Task ApplyAsync_WhenPayoutSucceeds_SendsSignalRAndStatementEmail()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var seller = new ShadowUser
        {
            Id = sellerId,
            Email = "seller@example.com",
            FullName = "Tran Duc Linh",
            PayoutBankCode = "MB",
            PayoutAccountNumber = "0329952127",
            PayoutAccountName = "TRAN DUC LINH"
        };
        _dbContext.ShadowUsers.Add(seller);

        var @event = new Event
        {
            Id = Guid.NewGuid(),
            Name = "Anh Trai Say Hi 2026",
            OrganizerId = Guid.NewGuid(),
            Venue = "My Dinh",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        _dbContext.Events.Add(@event);

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = @event.Id,
            TierName = "VIP Zone A",
            OriginalPrice = 2500000m
        };
        _dbContext.TicketTiers.Add(tier);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            SellerId = sellerId,
            EventId = @event.Id,
            TierId = tier.Id,
            OriginalTicketCode = "ATSH-VIP-888",
            OriginalPrice = 2500000m,
            ResalePrice = 2500000m,
            ListingStatus = ListingStatus.Sold,
            VerificationStatus = VerificationStatus.Verified
        };
        _dbContext.ResaleListings.Add(listing);

        var escrowId = Guid.NewGuid();
        var escrow = new EscrowTransaction
        {
            Id = escrowId,
            ListingId = listing.Id,
            SellerId = sellerId,
            BuyerId = Guid.NewGuid(),
            OriginalTicketPrice = 2500000m,
            TotalBuyerPaid = 2575000m,
            BuyerFee = 75000m,
            SellerFee = 75000m,
            NetSellerPayout = 2425000m,
            PaymentReference = "TSREF-PAYOUT-TEST",
            Status = EscrowStatus.Releasing,
            InSettlementBuffer = true
        };
        _dbContext.EscrowTransactions.Add(escrow);

        var payout = new PayoutTransaction
        {
            Id = Guid.NewGuid(),
            EscrowId = escrowId,
            SellerId = sellerId,
            PayoutCode = "PO-TEST1234",
            Amount = 2425000m,
            RecipientBankCode = "MB",
            RecipientAccountNumber = "0329952127",
            RecipientAccountName = "TRAN DUC LINH",
            Status = PayoutStatus.Processing
        };
        _dbContext.PayoutTransactions.Add(payout);

        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ApplyAsync(escrowId, succeeded: true, bankReference: "FT26999888");

        // Assert
        Assert.True(result);

        // Verify DB updates
        var updatedEscrow = await _dbContext.EscrowTransactions.FindAsync(escrowId);
        Assert.NotNull(updatedEscrow);
        Assert.Equal(EscrowStatus.Released, updatedEscrow.Status);
        Assert.False(updatedEscrow.InSettlementBuffer);

        var updatedPayout = await _dbContext.PayoutTransactions.FirstOrDefaultAsync(p => p.EscrowId == escrowId);
        Assert.NotNull(updatedPayout);
        Assert.Equal(PayoutStatus.Success, updatedPayout.Status);
        Assert.Equal("FT26999888", updatedPayout.BankReferenceCode);

        // Verify SignalR Notifier called
        _mockNotifier.Verify(n => n.NotifyPayoutCompletedAsync(
            sellerId,
            escrowId,
            listing.Id,
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify Email Sent
        _mockEmailService.Verify(e => e.SendEmailAsync(
            "seller@example.com",
            It.Is<string>(s => s.Contains("PO-TEST1234")),
            It.Is<string>(b => b.Contains("Mock Payout Statement Email")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
