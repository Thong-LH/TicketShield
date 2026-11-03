using Microsoft.EntityFrameworkCore;
using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// US-3.6_MF03: QA Suite - Đối soát thanh toán trùng (Duplicate Payment Reconciliation & Idempotency)
/// Kiểm thử tự động toàn diện: Webhook SePay idempotency, thanh toán trùng,
/// thanh toán trễ sau khi hết hạn giữ chỗ / đã huỷ, thiếu tiền, thừa tiền, bỏ qua giao dịch tiền ra.
/// </summary>
public class DuplicatePaymentReconciliationQATests
{
    private static (TicketShieldDbContext db, ResaleListing listing, EscrowTransaction escrow) CreateFixture(
        string paymentReference = "TSQA123456",
        decimal totalBuyerPaid = 1_050_000m,
        EscrowStatus escrowStatus = EscrowStatus.Pending,
        ListingStatus listingStatus = ListingStatus.Transacting,
        DateTimeOffset? unlockAt = null)
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new TicketShieldDbContext(options);

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "QA Organizer",
            OfficialEmail = "organizer@qa.test"
        };

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "QA Super Concert 2026",
            Venue = "My Dinh Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(10),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(9),
            Organizer = organizer
        };

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierName = "VIP",
            OriginalPrice = 1_000_000m,
            Event = ev
        };

        var seller = new ShadowUser { Id = Guid.NewGuid(), Email = "seller@qa.test", FullName = "Nguyen Seller" };
        var buyer = new ShadowUser { Id = Guid.NewGuid(), Email = "buyer@qa.test", FullName = "Tran Buyer" };

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = listingStatus,
            Event = ev,
            Tier = tier
        };

        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_000_000m,
            BuyerFee = 50_000m,
            SellerFee = 30_000m,
            TotalBuyerPaid = totalBuyerPaid,
            NetSellerPayout = 970_000m,
            PaymentReference = paymentReference,
            Status = escrowStatus,
            UnlockAt = unlockAt ?? DateTimeOffset.UtcNow.AddMinutes(10),
            RecipientName = "Tran Buyer",
            RecipientEmail = "buyer@qa.test"
        };

        listing.EscrowTransaction = escrow;

        db.Organizers.Add(organizer);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(seller, buyer);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        db.SaveChanges();

        return (db, listing, escrow);
    }

    private static Mock<ITicketVerificationService> CreateMockVerificationService()
    {
        var mock = new Mock<ITicketVerificationService>();
        mock.Setup(s => s.TransferOwnershipByListingId(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Transferred,
                NewTicket = new TicketSnapshot
                {
                    Ticket = new TicketReference
                    {
                        TicketCode = "TCK-NEW-RECON-001"
                    }
                }
            });
        return mock;
    }

    [Fact]
    public async Task Webhook_FirstValidPayment_LocksEscrow_IssuesTicket_ReturnsNonDuplicate()
    {
        // Arrange
        var (db, listing, escrow) = CreateFixture();
        var mockVerification = CreateMockVerificationService();
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10001,
            Gateway = "Vietcombank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "123456789",
            Code = null,
            Content = $"THANH TOAN {escrow.PaymentReference} CHO VE CONCERT",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "FT260010001"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.False(response.Data!.IsIdempotentDuplicate);
        Assert.Equal("Locked", response.Data.EscrowStatus);
        Assert.Equal("Sold", response.Data.ListingStatus);
        Assert.Equal("FT260010001", response.Data.BankTransactionReference);

        // Verify DB updates
        db.ChangeTracker.Clear();
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);

        Assert.Equal(EscrowStatus.Locked, updatedEscrow.Status);
        Assert.Equal(ListingStatus.Sold, updatedListing.ListingStatus);
        Assert.True(updatedEscrow.InSettlementBuffer);
        Assert.Equal("FT260010001", updatedEscrow.BankTransactionReference);
        Assert.Equal("TCK-NEW-RECON-001", updatedEscrow.NewTicketCode);
    }

    [Fact]
    public async Task Webhook_DuplicatePayment_SameBankTxRef_ReturnsIdempotentDuplicate_DoesNotReissue()
    {
        // Arrange - Escrow is ALREADY Locked with bank transaction reference
        var (db, _, escrow) = CreateFixture(escrowStatus: EscrowStatus.Locked, listingStatus: ListingStatus.Sold);
        escrow.BankTransactionReference = "FT260010001";
        escrow.NewTicketCode = "TCK-NEW-RECON-001";
        await db.SaveChangesAsync();

        var mockVerification = CreateMockVerificationService();
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10001,
            Gateway = "Vietcombank",
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "FT260010001" // Identical bank reference
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.True(response.Data!.IsIdempotentDuplicate);
        Assert.Contains("Payment Idempotency", response.Message);

        // gRPC transfer must NOT be called again
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Webhook_DuplicatePayment_AlreadyLockedEscrow_WithDifferentBankTx_ReturnsIdempotentDuplicate()
    {
        // Arrange - Escrow is already locked
        var (db, _, escrow) = CreateFixture(escrowStatus: EscrowStatus.Locked, listingStatus: ListingStatus.Sold);
        escrow.BankTransactionReference = "FT_FIRST_TX";
        await db.SaveChangesAsync();

        var mockVerification = CreateMockVerificationService();
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10002,
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "FT_SECOND_TX"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.True(response.Data!.IsIdempotentDuplicate);
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Webhook_DuplicatePayment_AlreadyRefundQueued_ReturnsIdempotentDuplicate()
    {
        // Arrange - Escrow is already in RefundQueued
        var (db, _, escrow) = CreateFixture(escrowStatus: EscrowStatus.RefundQueued, listingStatus: ListingStatus.Verified);
        escrow.BankTransactionReference = "FT_PREVIOUS_REFUND";
        await db.SaveChangesAsync();

        var mockVerification = CreateMockVerificationService();
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10003,
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "FT_PREVIOUS_REFUND"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.True(response.Data!.IsIdempotentDuplicate);
    }

    [Fact]
    public async Task Webhook_LatePayment_ArrivesAfterHoldExpired_RevertsListingToVerified_QueuesRefund()
    {
        // Arrange - Hold expired 5 minutes ago
        var expiredUnlockAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (db, listing, escrow) = CreateFixture(unlockAt: expiredUnlockAt);
        var mockVerification = CreateMockVerificationService();
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10004,
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "FT_LATE_001"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("RefundQueued", response.Data!.EscrowStatus);
        Assert.Contains("hết hạn", response.Message);

        db.ChangeTracker.Clear();
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);

        Assert.Equal(EscrowStatus.RefundQueued, updatedEscrow.Status);
        Assert.False(updatedEscrow.InSettlementBuffer);
        Assert.Equal(ListingStatus.Verified, updatedListing.ListingStatus); // Listing is back on sale

        // gRPC transfer must NOT be called for late payment
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Webhook_LatePayment_WhenEscrowWasCancelledByBuyer_QueuesRefundSafely()
    {
        // Arrange - Buyer previously cancelled hold, but money arrived
        var (db, _, escrow) = CreateFixture(escrowStatus: EscrowStatus.Cancelled, listingStatus: ListingStatus.Verified);
        var mockVerification = CreateMockVerificationService();
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10005,
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "FT_CANCELLED_001"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("RefundQueued", response.Data!.EscrowStatus);

        db.ChangeTracker.Clear();
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.RefundQueued, updatedEscrow.Status);
        Assert.False(updatedEscrow.InSettlementBuffer);
    }

    [Fact]
    public async Task Webhook_Underpayment_ThrowsBusinessRuleViolation_EscrowRemainsPending()
    {
        // Arrange - TotalBuyerPaid is 1,050,000; buyer transfers only 500,000
        var (db, _, escrow) = CreateFixture(totalBuyerPaid: 1_050_000m);
        var handler = new ProcessSePayWebhookCommandHandler(db);

        var payload = new SePayWebhookRequest
        {
            Id = 10006,
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 500_000m,
            ReferenceCode = "FT_UNDER_001"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None));
        Assert.Contains("nhỏ hơn tổng số tiền cần trả", ex.Message);

        db.ChangeTracker.Clear();
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Pending, updatedEscrow.Status);
    }

    [Fact]
    public async Task Webhook_Overpayment_SuccessfullyLocksEscrow()
    {
        // Arrange - TotalBuyerPaid is 1,050,000; buyer transfers 1,200,000
        var (db, _, escrow) = CreateFixture(totalBuyerPaid: 1_050_000m);
        var mockVerification = CreateMockVerificationService();
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10007,
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 1_200_000m,
            ReferenceCode = "FT_OVER_001"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("Locked", response.Data!.EscrowStatus);
    }

    [Fact]
    public async Task Webhook_NonInboundTransfer_IgnoredWithoutModifyingDatabase()
    {
        // Arrange
        var (db, _, escrow) = CreateFixture();
        var handler = new ProcessSePayWebhookCommandHandler(db);

        var payload = new SePayWebhookRequest
        {
            Id = 10008,
            Content = $"RUT TIEN {escrow.PaymentReference}",
            TransferType = "out",
            TransferAmount = 1_000_000m
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("Ignored", response.Data!.EscrowStatus);

        db.ChangeTracker.Clear();
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Pending, updatedEscrow.Status);
    }

    [Fact]
    public async Task Webhook_MissingPaymentReference_ThrowsBusinessRuleViolation()
    {
        // Arrange
        var (db, _, _) = CreateFixture();
        var handler = new ProcessSePayWebhookCommandHandler(db);

        var payload = new SePayWebhookRequest
        {
            Id = 10009,
            Content = "CHUYEN TIEN CHO BAN TIEN AN TRUA",
            TransferType = "in",
            TransferAmount = 100_000m
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None));
        Assert.Contains("Không thể tìm thấy mã thanh toán hợp lệ", ex.Message);
    }

    [Fact]
    public async Task Webhook_NonExistentPaymentReference_ThrowsNotFoundException()
    {
        // Arrange
        var (db, _, _) = CreateFixture();
        var handler = new ProcessSePayWebhookCommandHandler(db);

        var payload = new SePayWebhookRequest
        {
            Id = 10010,
            Content = "THANH TOAN TS99999999 KHONG CO TRONG DB",
            TransferType = "in",
            TransferAmount = 500_000m
        };

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None));
    }
}
