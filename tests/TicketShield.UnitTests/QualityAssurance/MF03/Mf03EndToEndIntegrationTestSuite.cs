using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TicketShield.Application.Common.Configurations;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Application.Features.ResaleListings.Commands.ReleaseListingHold;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using TicketShield.Infrastructure.Workers;
using Xunit;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// TEST-3.3.1_Automated Integration Test Suite for MF-03 End-to-End Flows
/// Description: Bộ kiểm thử tự động xUnit cho Idempotent Webhook, gRPC Rollback và Background Worker nhả vé quá hạn 10 phút.
/// Bao gồm kiểm thử liên hoàn:
/// - Vòng đời giữ chỗ 10 phút (Hold 10min) -> Webhook SePay thanh toán -> Sang tên vé gRPC -> Idempotent duplicate webhook.
/// - Xử lý lỗi gRPC MockOrganizer (Saga Compensation rollback listing về Verified, escrow về RefundQueued, 0 vé mồ côi).
/// - Background Worker (ExpiredHoldReleaseWorker) tự động nhả vé khi hết hạn 10 phút cho người mua khác.
/// - Giao thoa giữa Worker nhả vé và Webhook trễ (Late Payment) để đưa tiền vào RefundQueued và chống bán trùng (Zero Overselling).
/// </summary>
public class Mf03EndToEndIntegrationTestSuite
{
    private static readonly VietQrSettings TestVietQrSettings = new()
    {
        Provider = "SePAY",
        BaseUrl = "https://my.sepay.vn/userapi",
        ApiKey = "sepay_e2e_key_2026",
        WebhookSecret = "sepay_e2e_secret_2026",
        BankBin = "970422", // MBBank
        AccountNumber = "0938434102",
        AccountName = "CONG TY TICKETSHIELD",
        QrTemplate = "compact2"
    };

    #region Fixture & Factory Helpers

    private static (TicketShieldDbContext db, DbContextOptions<TicketShieldDbContext> options, ShadowUser seller, ShadowUser buyer1, ShadowUser buyer2, Event ev, TicketTier tier) CreateFixture(
        DateTimeOffset? eventStartAt = null,
        string? dbName = null)
    {
        var databaseName = string.IsNullOrWhiteSpace(dbName) ? Guid.NewGuid().ToString() : dbName;
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName)
            .Options;

        var db = new TicketShieldDbContext(options);

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "V-Pop Global Live Organizer",
            OfficialEmail = "organizer@vpop.test"
        };

        var startAt = eventStartAt ?? DateTimeOffset.UtcNow.AddDays(10);

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "V-Pop Symphony Grand Arena 2026",
            Venue = "My Dinh National Stadium",
            EventStartAt = startAt,
            EventEndAt = startAt.AddHours(4),
            ResaleDeadline = startAt.AddHours(-2),
            Organizer = organizer
        };

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierName = "SVIP Diamond Zone A",
            OriginalPrice = 1_500_000m,
            Event = ev
        };

        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller.e2e@vpop.test",
            FullName = "Nguyen Hoang Seller"
        };

        var buyer1 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer1.e2e@vpop.test",
            FullName = "Tran Thi Buyer One"
        };

        var buyer2 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer2.e2e@vpop.test",
            FullName = "Le Van Buyer Two"
        };

        db.Organizers.Add(organizer);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(seller, buyer1, buyer2);
        db.SaveChanges();

        return (db, options, seller, buyer1, buyer2, ev, tier);
    }

    private class MockCurrentUserService(Guid? userId, string? email = "buyer1.e2e@vpop.test") : ICurrentUserService
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
            decimal buyerFee = Math.Round(resalePrice * 0.05m, 0); // 5% buyer fee
            decimal sellerFee = Math.Round(resalePrice * 0.03m, 0); // 3% seller fee
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

    private static Mock<ITicketVerificationService> CreateMockVerificationService(string newTicketCode = "TCK-NEW-E2E-001")
    {
        var mock = new Mock<ITicketVerificationService>();
        mock.Setup(s => s.TransferOwnershipByListingId(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
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

    private static ExpiredHoldReleaseWorker CreateWorker(DbContextOptions<TicketShieldDbContext> options)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITicketShieldDbContext>(_ => new TicketShieldDbContext(options));
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return new ExpiredHoldReleaseWorker(scopeFactory, NullLogger<ExpiredHoldReleaseWorker>.Instance);
    }

    private static SePayWebhookRequest CreateSePayPayload(string paymentReference, decimal amount, string bankRef = "FT_E2E_9999")
    {
        return new SePayWebhookRequest
        {
            Id = 99001,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = TestVietQrSettings.AccountNumber,
            Content = $"THANH TOAN {paymentReference} E2E TEST",
            TransferType = "in",
            TransferAmount = amount,
            ReferenceCode = bankRef
        };
    }

    #endregion

    #region Pillar 1: Idempotent Webhook End-to-End Flows

    [Fact]
    public async Task Flow1_HappyPath_Hold_To_WebhookPayment_To_NewTicket_And_DuplicateWebhook_ReturnsIdempotentDuplicate()
    {
        // Arrange
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var feeCalculator = new MockResaleFeeCalculator();
        var vietQrService = new VietQrService(Options.Create(TestVietQrSettings));
        var holdHandler = new HoldListingForPurchaseCommandHandler(db, feeCalculator, new MockCurrentUserService(buyer1.Id), vietQrService);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-E2E-001",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Verified,
            Event = ev,
            Tier = tier
        };
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync();

        // Step 1: Buyer 1 holds the listing (Giữ chỗ 10 phút)
        var holdResult = await holdHandler.Handle(new HoldListingForPurchaseCommand(
            listing.Id, recipientName: "Tran Thi Buyer One", recipientEmail: "buyer1.e2e@vpop.test"), CancellationToken.None);

        Assert.True(holdResult.Success);
        var paymentRef = holdResult.Data!.PaymentReference;
        var totalAmount = holdResult.Data.TotalBuyerPaid;
        Assert.Equal(1_575_000m, totalAmount); // 1.500.000 + 5% = 1.575.000 VNĐ

        // Verify Step 1 DB State
        var heldListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Transacting, heldListing.ListingStatus);
        var pendingEscrow = await db.EscrowTransactions.SingleAsync(e => e.ListingId == listing.Id);
        Assert.Equal(EscrowStatus.Pending, pendingEscrow.Status);
        Assert.Equal(paymentRef, pendingEscrow.PaymentReference);

        // Step 2: SePay sends Webhook #1 (Webhook thanh toán hợp lệ lần 1)
        const string bankRef = "FT_E2E_HAPPY_01";
        const string newTicketCode = "TCK-NEW-E2E-FINAL-001";
        var mockVerification = CreateMockVerificationService(newTicketCode);
        var mockNotifier = new Mock<IPaymentRealtimeNotifier>();
        var webhookHandler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object, mockNotifier.Object);

        var payload = CreateSePayPayload(paymentRef, totalAmount, bankRef: bankRef);
        var response1 = await webhookHandler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert Step 2: Xử lý thành công, cấp vé mới, khóa Escrow
        Assert.True(response1.Success);
        Assert.False(response1.Data!.IsIdempotentDuplicate);
        Assert.Equal("Locked", response1.Data.EscrowStatus);
        Assert.Equal("Sold", response1.Data.ListingStatus);
        Assert.Equal(bankRef, response1.Data.BankTransactionReference);

        db.ChangeTracker.Clear();
        var lockedEscrow = await db.EscrowTransactions.SingleAsync(e => e.ListingId == listing.Id);
        Assert.Equal(EscrowStatus.Locked, lockedEscrow.Status);
        Assert.Equal(newTicketCode, lockedEscrow.NewTicketCode);
        Assert.True(lockedEscrow.InSettlementBuffer);

        var soldListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Sold, soldListing.ListingStatus);

        // gRPC sang tên vé được gọi đúng 1 lần
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            listing.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Step 3: SePay resends duplicate Webhook #2 (Bắn lại webhook trùng mã giao dịch)
        var response2 = await webhookHandler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert Step 3: Nhận diện Payment Idempotency an toàn
        Assert.True(response2.Success);
        Assert.True(response2.Data!.IsIdempotentDuplicate);
        Assert.Equal("Locked", response2.Data.EscrowStatus);
        Assert.Equal("Sold", response2.Data.ListingStatus);
        Assert.Contains("Payment Idempotency", response2.Message);

        // Verify Final Database Integrity: Duy nhất 1 Escrow và 1 vé mới
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.EscrowTransactions.CountAsync(e => e.ListingId == listing.Id));
        Assert.Equal(1, await db.ResaleListings.CountAsync(l => l.Id == listing.Id));

        // gRPC sang tên tuyệt đối không bị gọi lại lần 2
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            listing.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Flow2_IdempotentWebhook_ConcurrentRace_SimultaneousWebhooks_ProduceSingleEscrowAndSingleTicket()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var (dbSeed, options, seller, buyer1, _, ev, tier) = CreateFixture(dbName: dbName);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-CONCUR-01",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting,
            Event = ev,
            Tier = tier
        };

        const string paymentRef = "TSCONCUR99";
        const string bankRef = "FT_CONCUR_RACE_01";
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_500_000m,
            BuyerFee = 75_000m,
            SellerFee = 45_000m,
            TotalBuyerPaid = 1_575_000m,
            NetSellerPayout = 1_455_000m,
            PaymentReference = paymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10),
            RecipientName = "Tran Thi Buyer One",
            RecipientEmail = "buyer1.e2e@vpop.test"
        };

        listing.EscrowTransaction = escrow;
        dbSeed.ResaleListings.Add(listing);
        dbSeed.EscrowTransactions.Add(escrow);
        await dbSeed.SaveChangesAsync();

        var mockVerification = CreateMockVerificationService("TCK-CONCUR-NEW-01");

        // Sử dụng 2 DbContext scopes cho 2 Webhook đồng thời
        using var db1 = new TicketShieldDbContext(options);
        using var db2 = new TicketShieldDbContext(options);

        var handler1 = new ProcessSePayWebhookCommandHandler(db1, null, null, null, mockVerification.Object);
        var handler2 = new ProcessSePayWebhookCommandHandler(db2, null, null, null, mockVerification.Object);

        var payload = CreateSePayPayload(paymentRef, 1_575_000m, bankRef: bankRef);

        // Act: Bắn 2 webhook đồng thời
        var task1 = handler1.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        var task2 = handler2.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        var results = await Task.WhenAll(task1, task2);

        // Assert: Cả 2 đều trả success, ít nhất 1 cái là duplicate
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Contains(results, r => r.Data!.IsIdempotentDuplicate);

        using var dbVerify = new TicketShieldDbContext(options);
        Assert.Equal(1, await dbVerify.EscrowTransactions.CountAsync(e => e.ListingId == listing.Id));
        var finalEscrow = await dbVerify.EscrowTransactions.SingleAsync(e => e.ListingId == listing.Id);
        Assert.Equal(EscrowStatus.Locked, finalEscrow.Status);
        Assert.Equal("TCK-CONCUR-NEW-01", finalEscrow.NewTicketCode);

        // gRPC transfer chỉ gọi tối đa 1 lần
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            listing.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.AtMostOnce);
    }

    [Fact]
    public async Task Flow3_IdempotentWebhook_BundleCombo_AtomicPaymentAndDuplicateSafety()
    {
        // Arrange: Gói combo gồm 2 vé (bundle)
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var bundleId = Guid.NewGuid();

        var listing1 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-BND-ORIG-01",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            BundleTotalTickets = 2,
            Event = ev,
            Tier = tier
        };

        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-BND-ORIG-02",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            BundleTotalTickets = 2,
            Event = ev,
            Tier = tier
        };

        const string paymentRef = "TSBNDCMB01"; // Đúng định dạng 10 ký tự
        const string bankRef = "FT_BUNDLE_E2E_01";
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing1.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 2_000_000m,
            BuyerFee = 100_000m,
            SellerFee = 60_000m,
            TotalBuyerPaid = 2_100_000m,
            NetSellerPayout = 1_940_000m,
            PaymentReference = paymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10),
            RecipientName = "Tran Thi Buyer One",
            RecipientEmail = "buyer1.e2e@vpop.test",
            BundleId = bundleId
        };

        listing1.EscrowTransaction = escrow;
        db.ResaleListings.AddRange(listing1, listing2);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing1.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Transferred,
                NewTicket = new TicketSnapshot { Ticket = new TicketReference { TicketCode = "NEW-BND-01" } }
            });

        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing2.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Transferred,
                NewTicket = new TicketSnapshot { Ticket = new TicketReference { TicketCode = "NEW-BND-02" } }
            });

        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);
        var payload = CreateSePayPayload(paymentRef, 2_100_000m, bankRef: bankRef);

        // Act 1: Bắn webhook thanh toán trọn gói
        var res1 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(res1.Success);
        Assert.False(res1.Data!.IsIdempotentDuplicate);
        Assert.Equal("Locked", res1.Data.EscrowStatus);

        // Act 2: Bắn webhook trùng lặp cho gói combo
        var res2 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(res2.Success);
        Assert.True(res2.Data!.IsIdempotentDuplicate);

        // Assert: Cả 2 vé trong combo đều Sold, 1 Escrow duy nhất với 2 mã vé
        db.ChangeTracker.Clear();
        var l1 = await db.ResaleListings.SingleAsync(l => l.Id == listing1.Id);
        var l2 = await db.ResaleListings.SingleAsync(l => l.Id == listing2.Id);
        Assert.Equal(ListingStatus.Sold, l1.ListingStatus);
        Assert.Equal(ListingStatus.Sold, l2.ListingStatus);

        var savedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Locked, savedEscrow.Status);
        Assert.Equal("NEW-BND-01,NEW-BND-02", savedEscrow.NewTicketCode);

        // gRPC gọi đúng 1 lần cho mỗi vé con
        mockVerification.Verify(s => s.TransferOwnershipByListingId(listing1.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        mockVerification.Verify(s => s.TransferOwnershipByListingId(listing2.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Pillar 2: gRPC Rollback End-to-End Flows

    [Fact]
    public async Task Flow4_GrpcRollback_WhenMockOrganizerUnavailable_RollsBackListingToVerified_AndQueuesFullBuyerRefund()
    {
        // Arrange: Giả lập MockOrganizer bị sập mạng khi sang tên
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-ROLL-01",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting,
            Event = ev,
            Tier = tier
        };

        const string paymentRef = "TSROLLUNA1"; // Đúng định dạng 10 ký tự
        const string bankRef = "FT_ROLL_UNAV_01";
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_500_000m,
            BuyerFee = 75_000m,
            SellerFee = 45_000m,
            TotalBuyerPaid = 1_575_000m,
            NetSellerPayout = 1_455_000m,
            PaymentReference = paymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10),
            RecipientName = "Tran Thi Buyer One",
            RecipientEmail = "buyer1.e2e@vpop.test"
        };

        listing.EscrowTransaction = escrow;
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "MockOrganizer server crash")));

        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);
        var payload = CreateSePayPayload(paymentRef, 1_575_000m, bankRef: bankRef);

        // Act 1: Bắn webhook thanh toán gặp lỗi sang tên vé
        var response1 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert 1: Kích hoạt Saga Compensation thành công
        Assert.True(response1.Success);
        Assert.False(response1.Data!.IsIdempotentDuplicate);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response1.Data.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), response1.Data.ListingStatus);
        Assert.Contains("Saga Compensation", response1.Message);

        // Verify Database Rollback an toàn 100%:
        db.ChangeTracker.Clear();
        var rolledBackListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var rolledBackEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, rolledBackListing.ListingStatus); // Vé trả về sàn an toàn
        Assert.Equal(EscrowStatus.RefundQueued, rolledBackEscrow.Status); // Hoàn 100% tiền Buyer
        Assert.False(rolledBackEscrow.InSettlementBuffer);
        Assert.Null(rolledBackEscrow.NewTicketCode); // Không sinh vé mồ côi
        Assert.Equal(bankRef, rolledBackEscrow.BankTransactionReference);

        // Act 2: Bắn lại webhook sau rollback -> Nhận diện an toàn, không gọi lại gRPC
        var response2 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(response2.Success);
        Assert.True(response2.Data!.IsIdempotentDuplicate);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response2.Data.EscrowStatus);

        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            listing.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Flow5_GrpcRollback_UnspecifiedOutcome_RejectsOldTicketFallback_ProtectsBuyerWithRefund()
    {
        // Arrange: FIX Lỗi 20 - MockOrganizer trả về TransferOutcome.Unspecified
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-UNSPEC-01",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting,
            Event = ev,
            Tier = tier
        };

        const string paymentRef = "TSUNSPEC02";
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_500_000m,
            BuyerFee = 75_000m,
            SellerFee = 45_000m,
            TotalBuyerPaid = 1_575_000m,
            NetSellerPayout = 1_455_000m,
            PaymentReference = paymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10),
            RecipientName = "Tran Thi Buyer One",
            RecipientEmail = "buyer1.e2e@vpop.test"
        };

        listing.EscrowTransaction = escrow;
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Unspecified
            });

        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);
        var payload = CreateSePayPayload(paymentRef, 1_575_000m);

        // Act
        var res = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert: Từ chối fallback sang vé cũ, rollback vé về Verified, hoàn tiền Buyer
        Assert.True(res.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), res.Data!.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), res.Data.ListingStatus);

        db.ChangeTracker.Clear();
        var currentEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.RefundQueued, currentEscrow.Status);
        Assert.Null(currentEscrow.NewTicketCode);
    }

    #endregion

    #region Pillar 3: Background Worker 10-Minute Expired Hold Auto-Release

    [Fact]
    public async Task Flow6_BackgroundWorker_HoldExpiresAfter10Minutes_AutoReleasesListingToVerified_ForAnotherBuyer()
    {
        // Arrange
        var (db, options, seller, buyer1, buyer2, ev, tier) = CreateFixture();
        var feeCalculator = new MockResaleFeeCalculator();
        var vietQrService = new VietQrService(Options.Create(TestVietQrSettings));
        var worker = CreateWorker(options);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-AUTO-01",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Verified,
            Event = ev,
            Tier = tier
        };
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync();

        // Step 1: Buyer 1 holds the ticket (Hold 10 phút)
        var holdHandler1 = new HoldListingForPurchaseCommandHandler(db, feeCalculator, new MockCurrentUserService(buyer1.Id), vietQrService);
        var hold1 = await holdHandler1.Handle(new HoldListingForPurchaseCommand(listing.Id), CancellationToken.None);
        Assert.True(hold1.Success);

        // Step 2: Buyer 2 cố tình mua khi vé đang Transacting -> Bị chặn
        var holdHandler2 = new HoldListingForPurchaseCommandHandler(db, feeCalculator, new MockCurrentUserService(buyer2.Id), vietQrService);
        var exBlocked = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            holdHandler2.Handle(new HoldListingForPurchaseCommand(listing.Id), CancellationToken.None));
        Assert.Contains("đang được giữ chỗ", exBlocked.Message);

        // Step 3: Mô phỏng 10 phút trôi qua không ai thanh toán (UnlockAt đã qua)
        var escrow1 = await db.EscrowTransactions.SingleAsync(e => e.ListingId == listing.Id);
        escrow1.UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-2); // Quá hạn 2 phút
        await db.SaveChangesAsync();

        // Step 4: Background Worker chạy quét
        var releasedCount = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);
        Assert.Equal(1, releasedCount);

        // Verify: Escrow 1 chuyển Expired, Listing tự động mở lại Verified
        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var updatedEscrow1 = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow1.Id);

        Assert.Equal(ListingStatus.Verified, updatedListing.ListingStatus);
        Assert.Equal(EscrowStatus.Expired, updatedEscrow1.Status);

        // Step 5: Bây giờ Buyer 2 giữ chỗ lại -> Thành công!
        var hold2 = await holdHandler2.Handle(new HoldListingForPurchaseCommand(listing.Id), CancellationToken.None);
        Assert.True(hold2.Success);
        Assert.NotEqual(hold1.Data!.PaymentReference, hold2.Data!.PaymentReference); // Sinh mã thanh toán mới

        var finalListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Transacting, finalListing.ListingStatus);
    }

    [Fact]
    public async Task Flow7_BackgroundWorker_HoldAutoRelease_WhenResaleDeadlinePassed_MarksListingExpired()
    {
        // Arrange: Sự kiện diễn ra trong 1 giờ tới (< 2h trước giờ G, đã qua cutoff bán lại)
        var nearEventStart = DateTimeOffset.UtcNow.AddMinutes(60);
        var (db, options, seller, buyer1, _, ev, tier) = CreateFixture(eventStartAt: nearEventStart);
        var worker = CreateWorker(options);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-CUTOFF-01",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting,
            Event = ev,
            Tier = tier
        };

        const string paymentRef = "TSCUTOFF01";
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_500_000m,
            BuyerFee = 75_000m,
            SellerFee = 45_000m,
            TotalBuyerPaid = 1_575_000m,
            NetSellerPayout = 1_455_000m,
            PaymentReference = paymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1) // Đã quá hạn giữ chỗ
        };

        listing.EscrowTransaction = escrow;
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        // Act: Worker quét nhả vé
        var released = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);
        Assert.Equal(1, released);

        // Assert: Do đã quá resale deadline (< 2h), Listing chuyển thành Expired thay vì Verified
        db.ChangeTracker.Clear();
        var currentListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Expired, currentListing.ListingStatus);
    }

    #endregion

    #region Pillar 4: Crossover Integration (Worker Auto-Release + Late Payment Webhook)

    [Fact]
    public async Task Flow8_Crossover_LatePaymentArrivesAfterWorkerAutoReleasedListing_QueuesRefundInsteadOfDoubleSelling()
    {
        // Kịch bản Crossover:
        // 1. Buyer 1 giữ vé nhưng không thanh toán trong 10 phút.
        // 2. Worker quét nhả vé về Verified.
        // 3. Buyer 2 vào giữ chỗ vé đó (Listing chuyển sang Transacting cho Buyer 2).
        // 4. SePay bắn webhook thanh toán muộn của Buyer 1.
        // 5. Hệ thống bảo vệ: Tiền Buyer 1 vào RefundQueued, không cấp vé trùng, Buyer 2 an toàn 100%!

        var (db, options, seller, buyer1, buyer2, ev, tier) = CreateFixture();
        var worker = CreateWorker(options);
        var mockVerification = CreateMockVerificationService("TCK-SHOULD-NOT-MINT-FOR-BUYER-1");

        // Listing ban đầu
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-CROSSOVER-01",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting,
            Event = ev,
            Tier = tier
        };

        // Escrow 1 của Buyer 1 (Đã quá hạn 10 phút)
        const string buyer1PaymentRef = "TSBUYER101"; // Đúng 10 ký tự
        var escrow1 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_500_000m,
            BuyerFee = 75_000m,
            SellerFee = 45_000m,
            TotalBuyerPaid = 1_575_000m,
            NetSellerPayout = 1_455_000m,
            PaymentReference = buyer1PaymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-5), // Đã hết hạn 5 phút trước
            RecipientName = "Buyer 1",
            RecipientEmail = "buyer1@test.vn"
        };

        listing.EscrowTransaction = escrow1;
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow1);
        await db.SaveChangesAsync();

        // Step 1: Worker chạy nhả vé của Buyer 1 về Verified
        await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var listingAfterRelease = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Verified, listingAfterRelease.ListingStatus);

        // Step 2: Buyer 2 giữ chỗ vé này
        const string buyer2PaymentRef = "TSBUYER201"; // Đúng 10 ký tự
        var escrow2 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer2.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_500_000m,
            BuyerFee = 75_000m,
            SellerFee = 45_000m,
            TotalBuyerPaid = 1_575_000m,
            NetSellerPayout = 1_455_000m,
            PaymentReference = buyer2PaymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10), // Đang còn hiệu lực cho Buyer 2
            RecipientName = "Buyer 2",
            RecipientEmail = "buyer2@test.vn"
        };
        listingAfterRelease.ListingStatus = ListingStatus.Transacting;
        listingAfterRelease.EscrowTransaction = escrow2;
        db.EscrowTransactions.Add(escrow2);
        await db.SaveChangesAsync();

        // Step 3: Webhook SePay của Buyer 1 chuyển khoản muộn gửi đến sau khi Escrow 1 đã bị Worker đánh dấu Expired
        var webhookHandler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);
        var latePayload = CreateSePayPayload(buyer1PaymentRef, 1_575_000m, bankRef: "FT_LATE_BUYER1");

        // Khi Escrow 1 đã ở trạng thái Expired, hệ thống từ chối khóa với BusinessRuleViolationException
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            webhookHandler.Handle(new ProcessSePayWebhookCommand(latePayload), CancellationToken.None));
        Assert.Contains("Expired", ex.Message);

        // Verify Anti-Overselling:
        db.ChangeTracker.Clear();
        var verifiedEscrow1 = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow1.Id);
        var verifiedEscrow2 = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow2.Id);
        var finalListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);

        // Escrow 1 vẫn ở trạng thái Expired, không cấp vé mới
        Assert.Equal(EscrowStatus.Expired, verifiedEscrow1.Status);
        Assert.Null(verifiedEscrow1.NewTicketCode);

        // Escrow 2 của Buyer 2 vẫn nguyên vẹn Pending
        Assert.Equal(EscrowStatus.Pending, verifiedEscrow2.Status);

        // Listing vẫn thuộc quyền giữ của Buyer 2 (Transacting)
        Assert.Equal(ListingStatus.Transacting, finalListing.ListingStatus);

        // gRPC TUYỆT ĐỐI không bao giờ cấp vé cho Buyer 1
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Flow8B_Crossover_LatePaymentArrivesWhenHoldWindowExpiredBeforeWorker_QueuesRefundDirectly()
    {
        // Kịch bản: Buyer 1 chuyển khoản sau khi hết 10 phút nhưng Worker chưa kịp chạy quét
        // Webhook handler phát hiện UnlockAt <= now, tự động đưa vào RefundQueued và hoàn Listing về Verified
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var mockVerification = new Mock<ITicketVerificationService>();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-LATE-01",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting,
            Event = ev,
            Tier = tier
        };

        const string paymentRef = "TSLATETX01"; // 10 ký tự
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_500_000m,
            BuyerFee = 75_000m,
            SellerFee = 45_000m,
            TotalBuyerPaid = 1_575_000m,
            NetSellerPayout = 1_455_000m,
            PaymentReference = paymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-3), // Hết hạn 3 phút trước nhưng Escrow vẫn Pending
            RecipientName = "Buyer 1",
            RecipientEmail = "buyer1@test.vn"
        };
        listing.EscrowTransaction = escrow;
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        var webhookHandler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);
        var latePayload = CreateSePayPayload(paymentRef, 1_575_000m, bankRef: "FT_LATE_BEFORE_WORKER");

        var response = await webhookHandler.Handle(new ProcessSePayWebhookCommand(latePayload), CancellationToken.None);

        Assert.True(response.Success);
        Assert.False(response.Data!.IsIdempotentDuplicate);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data.EscrowStatus);
        Assert.Equal(nameof(ListingStatus.Verified), response.Data.ListingStatus);
        Assert.Contains("hàng đợi hoàn tiền", response.Message);

        db.ChangeTracker.Clear();
        var refreshedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        var refreshedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);

        Assert.Equal(EscrowStatus.RefundQueued, refreshedEscrow.Status);
        Assert.Null(refreshedEscrow.NewTicketCode);
        Assert.Equal(ListingStatus.Verified, refreshedListing.ListingStatus);

        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Flow9_Crossover_BuyerCancelsHold_ThenLatePaymentArrives_QueuesRefundSafely()
    {
        // Kịch bản: Buyer chủ động hủy giữ vé (ReleaseListingHoldCommand), sau đó tiền mới vào tài khoản sàn
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var mockVerification = CreateMockVerificationService("TCK-SHOULD-NOT-MINT");

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-CANCEL-01",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting,
            Event = ev,
            Tier = tier
        };

        const string paymentRef = "TSRELEAS01"; // Đúng 10 ký tự
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_500_000m,
            BuyerFee = 75_000m,
            SellerFee = 45_000m,
            TotalBuyerPaid = 1_575_000m,
            NetSellerPayout = 1_455_000m,
            PaymentReference = paymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(8)
        };

        listing.EscrowTransaction = escrow;
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        // Step 1: Buyer bấm hủy giữ chỗ (Release hold)
        var releaseHandler = new ReleaseListingHoldCommandHandler(db, new MockCurrentUserService(buyer1.Id));
        var releaseResult = await releaseHandler.Handle(new ReleaseListingHoldCommand(listing.Id), CancellationToken.None);
        Assert.True(releaseResult.Success);

        db.ChangeTracker.Clear();
        var cancelledEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Cancelled, cancelledEscrow.Status);

        // Step 2: Tiền chuyển muộn của Buyer tới qua SePay Webhook
        var webhookHandler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);
        var payload = CreateSePayPayload(paymentRef, 1_575_000m, bankRef: "FT_LATE_AFTER_CANCEL");

        var webhookResponse = await webhookHandler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert: Tiền được đưa an toàn vào hàng đợi hoàn tiền (RefundQueued)
        Assert.True(webhookResponse.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), webhookResponse.Data!.EscrowStatus);

        db.ChangeTracker.Clear();
        var refundedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.RefundQueued, refundedEscrow.Status);
        Assert.Null(refundedEscrow.NewTicketCode);

        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
