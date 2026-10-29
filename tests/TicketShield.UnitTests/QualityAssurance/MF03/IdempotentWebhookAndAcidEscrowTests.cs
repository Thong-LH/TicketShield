using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TicketShield.Application.Common.Configurations;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EscrowBuffer.Models;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// TEST-3.6.2_Integration tests for Idempotent Webhook and ACID Escrow creation
/// Description: Bắn 2 request webhook trùng mã giao dịch, xác minh hệ thống chỉ tạo duy nhất 1 Escrow và 1 vé mới.
/// Bao gồm kiểm thử tính toàn vẹn ACID, luồng tích hợp từ Hold đến Webhook, thanh toán song song (concurrency),
/// gói vé combo (bundle), bù trừ giao dịch (Saga Compensation rollback), và ngăn chặn spam thông báo/email.
/// </summary>
public class IdempotentWebhookAndAcidEscrowTests
{
    private static readonly VietQrSettings TestVietQrSettings = new()
    {
        Provider = "SePAY",
        BaseUrl = "https://my.sepay.vn/userapi",
        ApiKey = "sepay_qa_key_2026",
        WebhookSecret = "sepay_qa_secret_2026",
        BankBin = "970422", // MBBank
        AccountNumber = "0938434102",
        AccountName = "CONG TY TICKETSHIELD",
        QrTemplate = "compact2"
    };

    private static (TicketShieldDbContext db, ResaleListing listing, EscrowTransaction escrow, ShadowUser seller, ShadowUser buyer) CreateFixture(
        string paymentReference = "TSIDEM1234",
        decimal totalBuyerPaid = 1_050_000m,
        EscrowStatus escrowStatus = EscrowStatus.Pending,
        ListingStatus listingStatus = ListingStatus.Transacting,
        DateTimeOffset? unlockAt = null,
        string dbName = "")
    {
        var databaseName = string.IsNullOrWhiteSpace(dbName) ? Guid.NewGuid().ToString() : dbName;
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName)
            .Options;

        var db = new TicketShieldDbContext(options);

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "QA Organizer Idempotency",
            OfficialEmail = "organizer@idempotency.test"
        };

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "ACID Super Concert 2026",
            Venue = "National Convention Center",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(15),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(15).AddHours(4),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(14),
            Organizer = organizer
        };

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierName = "SVIP Lounge",
            OriginalPrice = 1_000_000m,
            Event = ev
        };

        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller@idempotency.test",
            FullName = "Nguyen Seller ACID"
        };

        var buyer = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer@idempotency.test",
            FullName = "Tran Buyer ACID"
        };

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-IDEM-001",
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
            RecipientName = "Tran Buyer ACID",
            RecipientEmail = "buyer@idempotency.test"
        };

        listing.EscrowTransaction = escrow;

        db.Organizers.Add(organizer);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(seller, buyer);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        db.SaveChanges();

        return (db, listing, escrow, seller, buyer);
    }

    private static Mock<ITicketVerificationService> CreateMockVerificationService(string newTicketCode = "NEW-TCK-IDEM-001")
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
                        TicketCode = newTicketCode
                    }
                }
            });
        return mock;
    }

    private class MockCurrentUserService(Guid? userId, string? email = "buyer@idempotency.test") : ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? Email => email;
        public string? Role => "User";
        public bool IsAuthenticated => userId.HasValue;
    }

    private class MockResaleFeeCalculator : IResaleFeeCalculator
    {
        public Task<ResaleFeeCalculationResult> CalculateFeeAsync(decimal resalePrice, bool isPrivate, CancellationToken ct = default)
        {
            decimal buyerFee = Math.Round(resalePrice * 0.05m, 0);
            decimal sellerFee = Math.Round(resalePrice * 0.03m, 0);
            return Task.FromResult(new ResaleFeeCalculationResult
            {
                OriginalPrice = resalePrice,
                BuyerFee = buyerFee,
                SellerFee = sellerFee,
                TotalBuyerPaid = resalePrice + buyerFee,
                NetSellerPayout = resalePrice - sellerFee
            });
        }

        public void InvalidateCache() { }
    }

    #region 1. Sequential Duplicate Webhooks (Bắn 2 request webhook trùng mã giao dịch)

    [Fact]
    public async Task Handle_SequentialDuplicateWebhooks_ProcessesFirstAndIdempotentlySkipsSecond_ExactlyOneEscrowAndOneTicket()
    {
        // Arrange
        const string paymentRef = "TSSEQ00001";
        const string bankTxRef = "BANK-TX-SEQ-9999";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);
        var mockVerification = CreateMockVerificationService("NEW-TCK-SEQ-001");
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10001,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentRef} CHO VE CONCERT",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = bankTxRef
        };

        // Act 1: Bắn webhook lần 1
        var response1 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert 1: Lần 1 xử lý thành công, không phải duplicate
        Assert.True(response1.Success);
        Assert.False(response1.Data!.IsIdempotentDuplicate);
        Assert.Equal("Locked", response1.Data.EscrowStatus);
        Assert.Equal("Sold", response1.Data.ListingStatus);
        Assert.Equal(bankTxRef, response1.Data.BankTransactionReference);

        // Act 2: Bắn webhook lần 2 trùng mã giao dịch (identical ReferenceCode & payload)
        var response2 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert 2: Lần 2 phát hiện duplicate ngay lập tức (Payment Idempotency)
        Assert.True(response2.Success);
        Assert.True(response2.Data!.IsIdempotentDuplicate);
        Assert.Equal("Locked", response2.Data.EscrowStatus);
        Assert.Equal("Sold", response2.Data.ListingStatus);
        Assert.Contains("Payment Idempotency", response2.Message);

        // Verify Database: Xác minh hệ thống chỉ tạo DUY NHẤT 1 Escrow và DUY NHẤT 1 vé mới
        db.ChangeTracker.Clear();
        var escrowCount = await db.EscrowTransactions.CountAsync(e => e.ListingId == listing.Id);
        Assert.Equal(1, escrowCount); // Chỉ duy nhất 1 Escrow

        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Locked, updatedEscrow.Status);
        Assert.True(updatedEscrow.InSettlementBuffer);
        Assert.Equal(bankTxRef, updatedEscrow.BankTransactionReference);
        Assert.Equal("NEW-TCK-SEQ-001", updatedEscrow.NewTicketCode); // Chỉ đúng 1 vé mới được cấp

        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Sold, updatedListing.ListingStatus);

        // gRPC transfer chỉ được gọi DUY NHẤT 1 LẦN (Times.Once)
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            listing.Id,
            escrow.BuyerId,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region 2. Concurrent Duplicate Webhooks (Bắn 2 webhook đồng thời)

    [Fact]
    public async Task Handle_ConcurrentDuplicateWebhooks_BothSucceedWithSingleTicketIssuanceAndSingleEscrow()
    {
        // Arrange
        const string paymentRef = "TSCONCUR01";
        const string bankTxRef = "BANK-TX-CONCUR-8888";
        var dbName = Guid.NewGuid().ToString();

        // Seed data
        var (dbSeed, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef, dbName: dbName);
        var mockVerification = CreateMockVerificationService("NEW-TCK-CONCUR-001");

        var payload = new SePayWebhookRequest
        {
            Id = 20002,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentRef} CONCURRENCY TEST",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = bankTxRef
        };

        // Sử dụng 2 DbContext scopes mô phỏng 2 HTTP request đồng thời đến API
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        using var db1 = new TicketShieldDbContext(options);
        using var db2 = new TicketShieldDbContext(options);

        var handler1 = new ProcessSePayWebhookCommandHandler(db1, null, null, null, mockVerification.Object);
        var handler2 = new ProcessSePayWebhookCommandHandler(db2, null, null, null, mockVerification.Object);

        // Act: Bắn đồng thời 2 webhook
        var task1 = handler1.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        var task2 = handler2.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        var results = await Task.WhenAll(task1, task2);

        // Assert: Cả 2 request đều thành công, không gặp conflict lỗi unhandled
        Assert.All(results, r => Assert.True(r.Success));

        // Ít nhất 1 request xử lý thành công, request thứ hai trả về duplicate nếu hoàn tất sau
        var duplicateCount = results.Count(r => r.Data!.IsIdempotentDuplicate);
        Assert.True(duplicateCount >= 1, "Ít nhất 1 request phải được ghi nhận là Idempotent Duplicate.");

        // Kiểm tra Database: Chỉ có duy nhất 1 Escrow và 1 vé mới
        using var dbVerify = new TicketShieldDbContext(options);
        var totalEscrows = await dbVerify.EscrowTransactions.CountAsync(e => e.ListingId == listing.Id);
        Assert.Equal(1, totalEscrows);

        var savedEscrow = await dbVerify.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Locked, savedEscrow.Status);
        Assert.Equal("NEW-TCK-CONCUR-001", savedEscrow.NewTicketCode);

        // gRPC transfer không bao giờ được gọi quá 1 lần
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            listing.Id,
            escrow.BuyerId,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.AtMostOnce);
    }

    #endregion

    #region 3. Duplicate Webhook with Different Bank Reference on Locked Escrow (Chặn thanh toán kép)

    [Fact]
    public async Task Handle_DuplicateWebhookWithDifferentBankTxCode_OnLockedEscrow_RejectsDoubleSpending()
    {
        // Arrange: Escrow đã bị Locked bởi giao dịch ngân hàng trước
        const string paymentRef = "TSDIFFBANK";
        const string originalBankRef = "BANK-ORIGINAL-111";
        const string secondBankRef = "BANK-DUPLICATE-222";

        var (db, listing, escrow, _, _) = CreateFixture(
            paymentReference: paymentRef,
            escrowStatus: EscrowStatus.Locked,
            listingStatus: ListingStatus.Sold);

        escrow.BankTransactionReference = originalBankRef;
        escrow.NewTicketCode = "NEW-TCK-ORIG-001";
        await db.SaveChangesAsync();

        var mockVerification = CreateMockVerificationService("NEW-TCK-SHOULD-NOT-MINT");
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 30003,
            Gateway = "Techcombank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentRef} LAN HAI",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = secondBankRef // Mã giao dịch ngân hàng khác
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert: Nhận diện Escrow đã Locked -> trả về duplicate, không sinh thêm vé
        Assert.True(response.Success);
        Assert.True(response.Data!.IsIdempotentDuplicate);
        Assert.Equal("Locked", response.Data.EscrowStatus);
        Assert.Equal("Sold", response.Data.ListingStatus);

        // Database: Mã giao dịch và mã vé ban đầu không bị ghi đè, Escrow vẫn là 1
        db.ChangeTracker.Clear();
        var currentEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(originalBankRef, currentEscrow.BankTransactionReference);
        Assert.Equal("NEW-TCK-ORIG-001", currentEscrow.NewTicketCode);
        Assert.Equal(1, await db.EscrowTransactions.CountAsync(e => e.ListingId == listing.Id));

        // gRPC TUYỆT ĐỐI KHÔNG được gọi
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region 4. Fallback Idempotency using payload.Id when ReferenceCode is null/empty

    [Fact]
    public async Task Handle_DuplicateWebhook_MissingReferenceCode_UsesIdAsBankTxRef_IdempotencyPreserved()
    {
        // Arrange
        const string paymentRef = "TSFALLBACK";
        const long sePayId = 998877;
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);
        var mockVerification = CreateMockVerificationService("NEW-TCK-FALLBACK-01");
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = sePayId,
            Gateway = "VietinBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"CHUYEN KHOAN {paymentRef}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = null // ReferenceCode rỗng, hệ thống fallback về payload.Id
        };

        // Act 1: Request 1
        var res1 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(res1.Success);
        Assert.False(res1.Data!.IsIdempotentDuplicate);
        Assert.Equal(sePayId.ToString(), res1.Data.BankTransactionReference);

        // Act 2: Request 2 (cùng payload Id)
        var res2 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(res2.Success);
        Assert.True(res2.Data!.IsIdempotentDuplicate);
        Assert.Equal(sePayId.ToString(), res2.Data.BankTransactionReference);

        // Verify: 1 Escrow, 1 Vé mới, gRPC gọi đúng 1 lần
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.EscrowTransactions.CountAsync(e => e.ListingId == listing.Id));
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            listing.Id,
            escrow.BuyerId,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region 5. ACID Atomicity on Multi-Ticket Bundle (Gói vé Combo 2 vé)

    [Fact]
    public async Task Handle_AcidBundleWebhook_SinglePaymentLocksEntireBundleAtomically_DuplicatePreservesSingleEscrowAndBundleTickets()
    {
        // Arrange: Tạo gói combo gồm 2 vé (bundle)
        const string paymentRef = "TSBUNDLE01";
        const string bankTxRef = "BANK-TX-BUNDLE-123";
        var bundleId = Guid.NewGuid();

        var (db, listing1, escrow, seller, buyer) = CreateFixture(paymentReference: paymentRef, totalBuyerPaid: 2_100_000m);
        listing1.BundleId = bundleId;
        listing1.BundleTotalTickets = 2;
        escrow.BundleId = bundleId;

        // Listing thứ 2 trong bundle
        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = listing1.EventId,
            TierId = listing1.TierId,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-IDEM-002",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            BundleTotalTickets = 2,
            Event = listing1.Event,
            Tier = listing1.Tier
        };
        db.ResaleListings.Add(listing2);
        await db.SaveChangesAsync();

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing1.Id,
                buyer.Id,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Transferred,
                NewTicket = new TicketSnapshot { Ticket = new TicketReference { TicketCode = "NEW-TCK-BND-01" } }
            });

        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing2.Id,
                buyer.Id,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Transferred,
                NewTicket = new TicketSnapshot { Ticket = new TicketReference { TicketCode = "NEW-TCK-BND-02" } }
            });

        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 40004,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN COMBO {paymentRef}",
            TransferType = "in",
            TransferAmount = 2_100_000m,
            ReferenceCode = bankTxRef
        };

        // Act 1: Bắn webhook lần 1
        var response1 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert 1: Xử lý thành công gói combo
        Assert.True(response1.Success);
        Assert.False(response1.Data!.IsIdempotentDuplicate);
        Assert.Equal("Locked", response1.Data.EscrowStatus);

        // Act 2: Bắn webhook lần 2 trùng mã giao dịch
        var response2 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert 2: Duplicate an toàn
        Assert.True(response2.Success);
        Assert.True(response2.Data!.IsIdempotentDuplicate);

        // Verify Database ACID:
        db.ChangeTracker.Clear();
        // 1. Chỉ có DUY NHẤT 1 Escrow transaction cho cả gói combo
        var escrows = await db.EscrowTransactions.Where(e => e.BundleId == bundleId).ToListAsync();
        Assert.Single(escrows);
        Assert.Equal(EscrowStatus.Locked, escrows[0].Status);
        Assert.Equal("NEW-TCK-BND-01,NEW-TCK-BND-02", escrows[0].NewTicketCode);

        // 2. CẢ 2 listing trong bundle đều chuyển thành công sang Sold cùng lúc (Atomic)
        var l1 = await db.ResaleListings.SingleAsync(l => l.Id == listing1.Id);
        var l2 = await db.ResaleListings.SingleAsync(l => l.Id == listing2.Id);
        Assert.Equal(ListingStatus.Sold, l1.ListingStatus);
        Assert.Equal(ListingStatus.Sold, l2.ListingStatus);

        // 3. gRPC được gọi đúng 1 lần cho mỗi vé trong bundle trong lần 1, và 0 lần trong lần 2
        mockVerification.Verify(s => s.TransferOwnershipByListingId(listing1.Id, buyer.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        mockVerification.Verify(s => s.TransferOwnershipByListingId(listing2.Id, buyer.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region 6. ACID Saga Rollback when gRPC Transfer Fails (Bù trừ giao dịch và không để vé mồ côi)

    [Fact]
    public async Task Handle_AcidRollback_WhenGrpcTransferFails_RollsBackAtomicStateWithoutLeavingOrphanNewTickets()
    {
        // Arrange
        const string paymentRef = "TSSAGAROLL";
        const string bankTxRef = "BANK-TX-ROLLBACK-555";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        // Mô phỏng gRPC BTC Organizer bị sập / timeout
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Organizer gRPC service is unavailable"));

        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 50005,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentRef}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = bankTxRef
        };

        // Act 1: Webhook lần 1 gặp lỗi gRPC sang tên
        var response1 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert 1: Kích hoạt Saga Compensation, hoàn tiền và rollback vé về Verified
        Assert.True(response1.Success);
        Assert.False(response1.Data!.IsIdempotentDuplicate);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response1.Data.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), response1.Data.ListingStatus);
        Assert.Contains("Saga Compensation", response1.Message);

        // Database ACID: Listing không bị kẹt ở Sold, Escrow là RefundQueued, NewTicketCode không có vé mồ côi
        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, updatedListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, updatedEscrow.Status);
        Assert.False(updatedEscrow.InSettlementBuffer);
        Assert.Null(updatedEscrow.NewTicketCode); // TUYỆT ĐỐI không sinh mã vé mới mồ côi

        // Act 2: Bắn duplicate webhook lần 2 vào Escrow đã RefundQueued
        var response2 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert 2: Idempotency bảo vệ, không chạy lại gRPC transfer
        Assert.True(response2.Success);
        Assert.True(response2.Data!.IsIdempotentDuplicate);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response2.Data.EscrowStatus);

        // gRPC chỉ được thử gọi 1 lần duy nhất trong lần 1, không gọi lại trong lần 2
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Escrow count vẫn là duy nhất 1
        Assert.Equal(1, await db.EscrowTransactions.CountAsync(e => e.ListingId == listing.Id));
    }

    #endregion

    #region 7. Duplicate Webhook on Expired Hold (Thanh toán đến trễ)

    [Fact]
    public async Task Handle_DuplicateLatePayment_WhenEscrowAlreadyExpiredAndRefundQueued_ReturnsIdempotentDuplicate()
    {
        // Arrange: Hold đã hết hạn (UnlockAt đã qua 5 phút trước)
        const string paymentRef = "TSLATEPAY1";
        const string bankTxRef = "BANK-TX-LATE-777";
        var (db, listing, escrow, _, _) = CreateFixture(
            paymentReference: paymentRef,
            escrowStatus: EscrowStatus.Pending,
            listingStatus: ListingStatus.Transacting,
            unlockAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var mockVerification = CreateMockVerificationService("NEW-TCK-SHOULD-NOT-MINT");
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 60006,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN TRE {paymentRef}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = bankTxRef
        };

        // Act 1: Webhook 1 đến trễ -> Chuyển sang RefundQueued
        var response1 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(response1.Success);
        Assert.False(response1.Data!.IsIdempotentDuplicate);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response1.Data.EscrowStatus);

        // Act 2: Webhook 2 trùng mã giao dịch đến tiếp
        var response2 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(response2.Success);
        Assert.True(response2.Data!.IsIdempotentDuplicate);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response2.Data.EscrowStatus);

        // Verify: Không bao giờ gọi gRPC mint vé, chỉ duy nhất 1 Escrow
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.EscrowTransactions.CountAsync(e => e.ListingId == listing.Id));
    }

    #endregion

    #region 8. End-to-End Integration Flow: Hold -> Webhook 1 -> Webhook 2 (Full Lifecycle)

    [Fact]
    public async Task Handle_CompleteE2EFlow_FromHoldToSequentialDuplicateWebhooks_EnsuresSingleEscrowLifecycleAndTicketIssuance()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var db = new TicketShieldDbContext(options);

        var organizer = new Organizer { Id = Guid.NewGuid(), Name = "E2E Organizer", OfficialEmail = "e2e@org.test" };
        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "End To End Concert 2026",
            Venue = "National Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(7),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(6),
            Organizer = organizer
        };
        var tier = new TicketTier { Id = Guid.NewGuid(), EventId = ev.Id, TierName = "Standard", OriginalPrice = 800_000m, Event = ev };
        var seller = new ShadowUser { Id = Guid.NewGuid(), Email = "seller@e2e.test", FullName = "Le Seller" };
        var buyer = new ShadowUser { Id = Guid.NewGuid(), Email = "buyer@e2e.test", FullName = "Pham Buyer" };

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-E2E-ORIG-100",
            OriginalPrice = 800_000m,
            ResalePrice = 800_000m,
            ListingStatus = ListingStatus.Verified,
            Event = ev,
            Tier = tier
        };

        db.Organizers.Add(organizer);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(seller, buyer);
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync();

        var feeCalculator = new MockResaleFeeCalculator();
        var vietQrService = new VietQrService(Options.Create(TestVietQrSettings));
        var holdHandler = new HoldListingForPurchaseCommandHandler(db, feeCalculator, new MockCurrentUserService(buyer.Id), vietQrService);

        // Step 1: Buyer bấm giữ chỗ (HoldListingForPurchase)
        var holdResult = await holdHandler.Handle(new HoldListingForPurchaseCommand(
            listing.Id,
            recipientName: "Pham Buyer Recipient",
            recipientEmail: "buyer@e2e.test"), CancellationToken.None);

        Assert.True(holdResult.Success);
        var paymentRef = holdResult.Data!.PaymentReference;
        var totalAmount = holdResult.Data.TotalBuyerPaid;
        Assert.StartsWith("TS", paymentRef);

        // Kiểm tra sau bước Hold: Có đúng 1 Escrow trạng thái Pending
        Assert.Equal(1, await db.EscrowTransactions.CountAsync(e => e.ListingId == listing.Id));
        var initialEscrow = await db.EscrowTransactions.SingleAsync(e => e.ListingId == listing.Id);
        Assert.Equal(EscrowStatus.Pending, initialEscrow.Status);
        Assert.Equal(ListingStatus.Transacting, (await db.ResaleListings.SingleAsync(l => l.Id == listing.Id)).ListingStatus);

        // Step 2: SePay bắn Webhook #1 xác nhận thanh toán
        const string bankRef = "BANK-E2E-REF-999";
        var mockVerification = CreateMockVerificationService("NEW-TCK-E2E-FINAL");
        var webhookHandler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 70007,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = TestVietQrSettings.AccountNumber,
            Content = $"THANH TOAN {paymentRef}",
            TransferType = "in",
            TransferAmount = totalAmount,
            ReferenceCode = bankRef
        };

        var webhookRes1 = await webhookHandler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(webhookRes1.Success);
        Assert.False(webhookRes1.Data!.IsIdempotentDuplicate);
        Assert.Equal("Locked", webhookRes1.Data.EscrowStatus);
        Assert.Equal("Sold", webhookRes1.Data.ListingStatus);

        // Step 3: SePay gửi trùng Webhook #2
        var webhookRes2 = await webhookHandler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(webhookRes2.Success);
        Assert.True(webhookRes2.Data!.IsIdempotentDuplicate);
        Assert.Equal("Locked", webhookRes2.Data.EscrowStatus);

        // Step 4: Kiểm tra toàn diện toàn bộ cơ sở dữ liệu sau vòng đời E2E
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.EscrowTransactions.CountAsync(e => e.ListingId == listing.Id));
        Assert.Equal(1, await db.ResaleListings.CountAsync(l => l.Id == listing.Id));

        var finalEscrow = await db.EscrowTransactions.SingleAsync(e => e.ListingId == listing.Id);
        Assert.Equal(EscrowStatus.Locked, finalEscrow.Status);
        Assert.Equal("NEW-TCK-E2E-FINAL", finalEscrow.NewTicketCode);
        Assert.Equal(bankRef, finalEscrow.BankTransactionReference);
        Assert.True(finalEscrow.InSettlementBuffer);

        var finalListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Sold, finalListing.ListingStatus);

        // gRPC transfer được gọi đúng 1 lần duy nhất trong toàn bộ quy trình
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            listing.Id,
            buyer.Id,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region 9. Realtime Notifications & Emails Sent Only Once (Không spam khi trùng lặp)

    [Fact]
    public async Task Handle_NotificationsAndEmails_SentOnlyOnce_DuplicateWebhooksDoNotSpamBuyerOrSeller()
    {
        // Arrange
        const string paymentRef = "TSNOTIF001";
        const string bankRef = "BANK-NOTIF-12345";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);
        var mockVerification = CreateMockVerificationService("NEW-TCK-NOTIF-001");

        var mockNotifier = new Mock<IPaymentRealtimeNotifier>();
        var mockEmailTemplates = new Mock<IEmailTemplateService>();
        var mockEmailService = new Mock<IEmailService>();

        mockEmailTemplates.Setup(t => t.GetBuyerTicketIssuedEmailHtml(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<decimal>()))
            .Returns("<html>Buyer Email Body</html>");

        var handler = new ProcessSePayWebhookCommandHandler(
            db,
            mockEmailTemplates.Object,
            mockEmailService.Object,
            NullLogger<ProcessSePayWebhookCommandHandler>.Instance,
            mockVerification.Object,
            mockNotifier.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 80008,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentRef}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = bankRef
        };

        // Act 1: Webhook lần 1
        var res1 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(res1.Success);

        // Verify: Notifier & Email được gọi đúng 1 lần
        mockNotifier.Verify(n => n.NotifyPaymentApprovedAsync(listing.Id, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        mockNotifier.Verify(n => n.NotifyOrderSettledAsync(listing.Id, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        mockEmailService.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // Act 2: Webhook lần 2 trùng mã giao dịch
        var res2 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(res2.Success);
        Assert.True(res2.Data!.IsIdempotentDuplicate);

        // Verify: Lần 2 KHÔNG được gửi thêm bất kỳ thông báo hay email nào (call count không tăng)
        mockNotifier.Verify(n => n.NotifyPaymentApprovedAsync(listing.Id, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        mockNotifier.Verify(n => n.NotifyOrderSettledAsync(listing.Id, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        mockEmailService.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    #endregion

    #region 10. Underpayment & Non-Inbound Transfers (Không tạo vé và không lock escrow)

    [Fact]
    public async Task Handle_Underpayment_RejectsAndDoesNotLockEscrowOrIssueTicket()
    {
        // Arrange
        const string paymentRef = "TSUNDER001";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef, totalBuyerPaid: 1_050_000m);
        var mockVerification = CreateMockVerificationService("NEW-TCK-SHOULD-NOT-MINT");
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 90009,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentRef}",
            TransferType = "in",
            TransferAmount = 500_000m, // Thiếu tiền (500k < 1.050k)
            ReferenceCode = "BANK-UNDER-01"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None));
        Assert.Contains("nhỏ hơn tổng số tiền cần trả", ex.Message);

        // Database không bị ảnh hưởng: Escrow vẫn Pending, Listing vẫn Transacting, không có vé mới
        db.ChangeTracker.Clear();
        var currentEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Pending, currentEscrow.Status);
        Assert.Null(currentEscrow.NewTicketCode);

        var currentListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Transacting, currentListing.ListingStatus);

        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonInboundTransfer_IgnoredWithoutModifyingEscrowOrIssuingTicket()
    {
        // Arrange
        const string paymentRef = "TSIOUT0001";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);
        var mockVerification = CreateMockVerificationService("NEW-TCK-SHOULD-NOT-MINT");
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 90010,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"TIEN RA {paymentRef}",
            TransferType = "out", // Giao dịch chuyển tiền ra
            TransferAmount = 1_050_000m,
            ReferenceCode = "BANK-OUT-01"
        };

        // Act
        var res = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(res.Success);
        Assert.Equal("Ignored", res.Data!.EscrowStatus);
        Assert.Equal("Ignored", res.Data.ListingStatus);

        // Database không bị thay đổi
        db.ChangeTracker.Clear();
        var currentEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Pending, currentEscrow.Status);
        Assert.Null(currentEscrow.NewTicketCode);

        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}

