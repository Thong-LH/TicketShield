using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using TicketShield.API.Hubs;
using TicketShield.API.Services;
using TicketShield.Application.Common.Configurations;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessTicketUsedWebhook;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Settlement.API.Data;
using TicketShield.Settlement.API.Gateway;
using TicketShield.Settlement.API.Payouts;
using Xunit;
using PayoutIdempotency = TicketShield.Application.Common.Models.PayoutIdempotency;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// TEST-5.5.3: End-to-End Integration Test for Bundle and Settlement Flow
/// Description: Test luồng hoàn chỉnh: Đăng bán Combo -> Mua trọn gói -> Webhook VietQR -> gRPC sang tên N vé -> Quét cổng USED -> Giải ngân -> SignalR.
/// [Phụ thuộc: sau BE-CORE-5.2.4c | Mở khóa cho: Đóng Sprint 03]
///
/// Các giai đoạn tích hợp toàn trình (7 Stages):
/// 1. Đăng bán Combo: Khởi tạo gói vé Combo N vé (IsBundleAllOrNothing = true, BundleTotalTickets = N).
/// 2. Mua trọn gói: Giữ chỗ trọn gói qua anchor listing, khóa đồng thời N vé sang Transacting, tạo Escrow duy nhất tính gộp phí.
/// 3. Webhook VietQR: Nhận thanh toán SePay chuẩn xác, đối soát toàn vẹn số tiền gộp (TotalBuyerPaid).
/// 4. gRPC sang tên N vé: Đổi chủ toàn bộ N vé trong bundle qua gRPC sang tên BTC Organizer, cập nhật Sold, lưu mã vé mới.
/// 5. Quét cổng USED: Soát vé tại cổng BTC (SCRUM-170 / BE-CORE-5.2.4c), chỉ kích hoạt giải ngân khi 100% vé trong gói đã USED.
/// 6. Giải ngân (Settlement): Chuyển Escrow sang Releasing, Outbox PayoutRequestedEvent, Settlement API chuyển tiền NAPAS 247.
/// 7. SignalR: Phát sóng sự kiện thời gian thực PayoutCompleted tới nhóm Người bán (seller_{sellerId}) và Tin bán (listing_{listingId}).
/// </summary>
public class BundleAndSettlementEndToEndIntegrationTests
{
    private static readonly VietQrSettings TestVietQrSettings = new()
    {
        Provider = "SePay",
        BankBin = "970422",
        AccountNumber = "0938434102",
        AccountName = "CONG TY TICKETSHIELD"
    };

    #region Fixture & Factory Helpers

    private static TicketShieldDbContext CreateInMemoryCoreDb()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TicketShieldDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<SettlementDbContext> CreateSettlementDbAsync()
    {
        var options = new DbContextOptionsBuilder<SettlementDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new SettlementDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static (
        Organizer organizer,
        Event ev,
        TicketTier tier,
        ShadowUser seller,
        ShadowUser buyer,
        Guid bundleId,
        List<ResaleListing> listings) SeedBundleListings(TicketShieldDbContext db, int ticketCount = 2, decimal unitPrice = 1_000_000m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var org = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "Ban To Chuc Festival",
            OfficialEmail = $"btc-{suffix}@festival.vn"
        };

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "EDM Super Festival 2026",
            Venue = "San Van Dong My Dinh",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(10),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(5),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(9)
        };

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierName = "GA COMBO",
            OriginalPrice = unitPrice
        };

        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = $"seller-{suffix}@ticketshield.vn",
            FullName = "Nguyen Van Seller",
            PayoutBankCode = "MB",
            PayoutAccountNumber = "0938434102",
            PayoutAccountName = "NGUYEN VAN SELLER"
        };

        var buyer = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = $"buyer-{suffix}@ticketshield.vn",
            FullName = "Tran Thi Buyer",
            PayoutBankCode = "VCB",
            PayoutAccountNumber = "0938434103",
            PayoutAccountName = "TRAN THI BUYER"
        };

        var bundleId = Guid.NewGuid();
        var listings = new List<ResaleListing>();

        for (var i = 1; i <= ticketCount; i++)
        {
            listings.Add(new ResaleListing
            {
                Id = Guid.NewGuid(),
                EventId = ev.Id,
                TierId = tier.Id,
                SellerId = seller.Id,
                OriginalTicketCode = $"BTC-ORIG-{suffix}-{i:00}",
                OriginalPrice = unitPrice,
                ResalePrice = unitPrice,
                ListingStatus = ListingStatus.Verified,
                BundleId = bundleId,
                IsBundleAllOrNothing = true,
                BundleTotalTickets = ticketCount,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
            });
        }

        db.Organizers.Add(org);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(seller, buyer);
        db.ResaleListings.AddRange(listings);
        db.SaveChanges();

        return (org, ev, tier, seller, buyer, bundleId, listings);
    }

    private sealed class StubCurrentUserService(Guid userId, string email = "buyer@test.local") : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email { get; } = email;
        public string? Role => "User";
        public bool IsAuthenticated => true;
    }

    private sealed class FixedFeeCalculator(decimal buyerPct = 0.05m, decimal sellerPct = 0.03m) : IResaleFeeCalculator
    {
        public Task<ResaleFeeCalculationResult> CalculateFeeAsync(decimal resalePrice, bool isPrivate, CancellationToken ct = default)
        {
            var buyerFee = Math.Round(resalePrice * buyerPct, 0);
            var sellerFee = Math.Round(resalePrice * sellerPct, 0);
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

    private static TransferOwnershipResponse CreateSuccessTransferResponse(string ticketCode = "TCK-SUCCESS-OFFICIAL")
    {
        return new TransferOwnershipResponse
        {
            Outcome = TransferOutcome.Transferred,
            NewTicket = new TicketSnapshot
            {
                Ticket = new TicketReference
                {
                    TicketCode = ticketCode
                }
            }
        };
    }

    private static SePayWebhookRequest CreateSePayPayload(
        string paymentReference,
        decimal amount,
        string bankRef = "FT_BUNDLE_12345",
        string transferType = "in")
    {
        return new SePayWebhookRequest
        {
            Id = 998877,
            Gateway = "SePay",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentReference} HE THONG TICKETSHIELD",
            TransferType = transferType,
            TransferAmount = amount,
            ReferenceCode = bankRef
        };
    }

    private sealed class StubCorePayoutReporter : ICorePayoutReporter
    {
        public Task<bool> ReportAsync(Guid escrowId, string idempotencyKey, bool succeeded, string? bankReference, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    /// <summary>
    /// Bộ xử lý giải ngân ký quỹ mô phỏng chuẩn nghiệp vụ CAS và tạo Outbox message
    /// </summary>
    private sealed class InMemoryEscrowPayoutSettler : IEscrowPayoutSettler
    {
        private readonly TicketShieldDbContext _db;

        public InMemoryEscrowPayoutSettler(TicketShieldDbContext db)
        {
            _db = db;
        }

        public async Task<bool> TrySettleAsync(Guid escrowId, CancellationToken cancellationToken = default)
        {
            var escrow = await _db.EscrowTransactions
                .Include(e => e.Seller)
                .FirstOrDefaultAsync(e => e.Id == escrowId, cancellationToken);

            if (escrow == null || escrow.Status != EscrowStatus.Locked)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            escrow.Status = EscrowStatus.Releasing;
            escrow.UpdatedAt = now;

            var payoutRequested = PayoutReleaseLetter.Create(escrow, escrow.Seller!);
            _db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = nameof(PayoutRequestedEvent),
                Payload = JsonSerializer.Serialize(payoutRequested),
                CreatedAt = now,
                UpdatedAt = now
            });

            var payout = await _db.PayoutTransactions.FirstOrDefaultAsync(p => p.EscrowId == escrow.Id, cancellationToken);
            if (payout == null)
            {
                payout = new PayoutTransaction
                {
                    Id = Guid.NewGuid(),
                    EscrowId = escrow.Id,
                    SellerId = escrow.SellerId,
                    PayoutCode = PayoutReleaseLetter.Code(escrow.Id),
                    CreatedAt = now
                };
                _db.PayoutTransactions.Add(payout);
            }

            PayoutReleaseLetter.Apply(payout, payoutRequested, now);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public Task<bool> TryResumeReleaseAsync(Guid escrowId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    #endregion

    #region Test Cases: Complete 7-Stage End-to-End Flow

    [Fact]
    public async Task EndToEnd_BundleOfTwoTickets_FullLifecycleFlow_FromPublishToSignalRPayout()
    {
        // =========================================================================
        // STAGE 1: ĐĂNG BÁN COMBO (Publish Bundle Listing)
        // =========================================================================
        await using var db = CreateInMemoryCoreDb();

        const int bundleTicketCount = 2;
        const decimal unitPrice = 1_000_000m;
        var (_, _, _, seller, buyer, bundleId, bundleListings) = SeedBundleListings(db, bundleTicketCount, unitPrice);

        Assert.Equal(bundleTicketCount, bundleListings.Count);
        Assert.All(bundleListings, l =>
        {
            Assert.Equal(ListingStatus.Verified, l.ListingStatus);
            Assert.Equal(bundleId, l.BundleId);
            Assert.True(l.IsBundleAllOrNothing);
        });

        // =========================================================================
        // STAGE 2: MUA TRỌN GÓI (Hold Bundle For Purchase)
        // =========================================================================
        var feeCalc = new FixedFeeCalculator();
        var currentUser = new StubCurrentUserService(buyer.Id, buyer.Email);
        var holdHandler = new HoldListingForPurchaseCommandHandler(db, feeCalc, currentUser);

        // Buyer nhấn giữ chỗ gói thông qua anchor listing (vé đầu tiên trong gói)
        var anchorListing = bundleListings[0];
        var holdResult = await holdHandler.Handle(new HoldListingForPurchaseCommand(anchorListing.Id), CancellationToken.None);

        Assert.True(holdResult.Success);
        Assert.Equal("Transacting", holdResult.Data.ListingStatus);
        Assert.Equal(bundleId, holdResult.Data.BundleId);
        Assert.Equal(bundleTicketCount, holdResult.Data.BundleTotalTickets);
        Assert.NotNull(holdResult.Data.PaymentReference);
        var paymentRef = holdResult.Data.PaymentReference!;

        // Xác minh toàn bộ N vé trong bundle đều bị khóa sang Transacting
        db.ChangeTracker.Clear();
        var updatedListingsAfterHold = await db.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.All(updatedListingsAfterHold, l => Assert.Equal(ListingStatus.Transacting, l.ListingStatus));

        // Xác minh đúng 1 Escrow duy nhất được tạo ra cho toàn bộ gói
        var bundleEscrows = await db.EscrowTransactions.Where(e => e.BundleId == bundleId).ToListAsync();
        Assert.Single(bundleEscrows);
        var escrow = bundleEscrows[0];
        Assert.Equal(EscrowStatus.Pending, escrow.Status);
        Assert.Equal(paymentRef, escrow.PaymentReference);

        // Tổng tiền gộp: 2 vé * 1.000.000đ = 2.000.000đ. Phí Buyer 5% = 100.000đ. Tổng trả = 2.100.000đ.
        const decimal expectedTotalPaid = 2_100_000m;
        const decimal expectedNetPayout = 1_940_000m; // Phí Seller 3% = 60.000đ. Net Payout = 1.940.000đ.
        Assert.Equal(expectedTotalPaid, escrow.TotalBuyerPaid);
        Assert.Equal(expectedNetPayout, escrow.NetSellerPayout);

        // =========================================================================
        // STAGE 3 & 4: WEBHOOK VIETQR & gRPC SANG TÊN N VÉ
        // =========================================================================
        var mockVerificationService = new Mock<ITicketVerificationService>();

        // Mock gRPC đổi chủ cho từng vé trong bundle
        var issuedNewCodes = new Dictionary<Guid, string>();
        for (var i = 0; i < bundleTicketCount; i++)
        {
            var lId = bundleListings[i].Id;
            var newCode = $"ORGANIZER-NEW-TICK-00{i + 1}";
            issuedNewCodes[lId] = newCode;

            mockVerificationService.Setup(v => v.TransferOwnershipByListingId(
                lId,
                buyer.Id,
                buyer.Email,
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessTransferResponse(newCode));
        }

        var sePayPayload = CreateSePayPayload(paymentRef, expectedTotalPaid);
        var webhookHandler = new ProcessSePayWebhookCommandHandler(
            db,
            null,
            null,
            null,
            mockVerificationService.Object);

        var webhookResult = await webhookHandler.Handle(new ProcessSePayWebhookCommand(sePayPayload), CancellationToken.None);

        Assert.True(webhookResult.Success);
        Assert.Equal("Locked", webhookResult.Data.EscrowStatus);
        Assert.Equal("Sold", webhookResult.Data.ListingStatus);

        // Xác minh gRPC được gọi đúng N lần (1 lần cho mỗi vé trong bundle)
        foreach (var listing in bundleListings)
        {
            mockVerificationService.Verify(v => v.TransferOwnershipByListingId(
                listing.Id, buyer.Id, buyer.Email, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        // Toàn bộ vé trong bundle chuyển sang Sold
        db.ChangeTracker.Clear();
        var soldListings = await db.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.All(soldListings, l => Assert.Equal(ListingStatus.Sold, l.ListingStatus));

        // Escrow chuyển sang trạng thái Locked, lưu chuỗi mã vé mới
        var lockedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Locked, lockedEscrow.Status);
        Assert.True(lockedEscrow.InSettlementBuffer);
        Assert.NotNull(lockedEscrow.NewTicketCode);

        var code1 = issuedNewCodes[bundleListings[0].Id];
        var code2 = issuedNewCodes[bundleListings[1].Id];
        Assert.Contains(code1, lockedEscrow.NewTicketCode);
        Assert.Contains(code2, lockedEscrow.NewTicketCode);

        // =========================================================================
        // STAGE 5: QUÉT CỔNG USED (Gate Entry Trigger - Anti Premature Release)
        // =========================================================================
        var settler = new InMemoryEscrowPayoutSettler(db);
        var mockPaymentNotifier = new Mock<IPaymentRealtimeNotifier>();
        var ticketUsedHandler = new ProcessTicketUsedWebhookCommandHandler(db, settler, mockPaymentNotifier.Object);

        // Bước 5.1: Quét vé thứ nhất tại cổng (Mới có 1/2 vé được dùng)
        var scan1Request = new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest { TicketCode = code1 });
        var scan1Result = await ticketUsedHandler.Handle(scan1Request, CancellationToken.None);

        Assert.True(scan1Result.Success);
        Assert.Contains("Đang chờ các vé còn lại", scan1Result.Message);

        // Escrow vẫn đang Locked (chưa đủ điều kiện giải ngân sớm)
        db.ChangeTracker.Clear();
        var escrowAfterScan1 = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Locked, escrowAfterScan1.Status);

        // Bước 5.2: Quét vé thứ hai tại cổng (Đủ 2/2 vé trong combo)
        var scan2Request = new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest { TicketCode = code2 });
        var scan2Result = await ticketUsedHandler.Handle(scan2Request, CancellationToken.None);

        Assert.True(scan2Result.Success);
        Assert.Contains("Kích hoạt giải ngân sớm thành công", scan2Result.Message);

        // Escrow được chuyển sang Releasing thành công
        db.ChangeTracker.Clear();
        var escrowAfterAllScans = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Releasing, escrowAfterAllScans.Status);

        // Kiểm tra Outbox Message giải ngân được sinh ra
        var outboxMessages = await db.OutboxMessages
            .Where(m => m.EventType == nameof(PayoutRequestedEvent) && m.Payload.Contains(escrow.Id.ToString()))
            .ToListAsync();
        Assert.Single(outboxMessages);

        var payoutLetter = JsonSerializer.Deserialize<PayoutRequestedEvent>(outboxMessages[0].Payload);
        Assert.NotNull(payoutLetter);
        Assert.Equal(expectedNetPayout, payoutLetter.Amount);
        Assert.Equal("MB", payoutLetter.BankCode);
        Assert.Equal("0938434102", payoutLetter.AccountNumber);

        // =========================================================================
        // STAGE 6: GIẢI NGÂN (Settlement via NAPAS 247 Gateway)
        // =========================================================================
        await using var settlementDb = await CreateSettlementDbAsync();
        var mockGateway = new MockNapasPayoutGateway();
        var settlementService = new SettlementPayoutService(settlementDb, mockGateway, new StubCorePayoutReporter());

        var payoutCommand = new PayoutCommand
        {
            EscrowId = payoutLetter.EscrowId,
            SellerId = payoutLetter.SellerId,
            Amount = payoutLetter.Amount,
            RetryCount = payoutLetter.RetryCount,
            IdempotencyKey = payoutLetter.IdempotencyKey,
            BankCode = payoutLetter.BankCode,
            AccountNumber = payoutLetter.AccountNumber,
            AccountName = payoutLetter.AccountName
        };

        var payoutResult = await settlementService.AcceptAsync(payoutCommand, CancellationToken.None);

        Assert.True(payoutResult.Accepted);
        Assert.Equal(TransferState.Succeeded, payoutResult.State);
        Assert.True(payoutResult.GatewayCalled);
        Assert.NotNull(payoutResult.BankReference);

        // Cập nhật trạng thái Escrow sang Released
        escrowAfterAllScans.Status = EscrowStatus.Released;
        escrowAfterAllScans.InSettlementBuffer = false;
        await db.SaveChangesAsync();

        // =========================================================================
        // STAGE 7: SIGNALR (Realtime Notification to Seller & Listing Groups)
        // =========================================================================
        var mockHubContext = new Mock<IHubContext<PaymentHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockGroup = new Mock<IClientProxy>();
        var mockUser = new Mock<IClientProxy>();

        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroup.Object);
        mockClients.Setup(c => c.User(It.IsAny<string>())).Returns(mockUser.Object);

        var signalRNotifier = new SignalRPayoutRealtimeNotifier(mockHubContext.Object);

        var notificationPayload = new
        {
            EscrowId = escrow.Id,
            ListingId = anchorListing.Id,
            SellerId = seller.Id,
            Amount = expectedNetPayout,
            BankReference = payoutResult.BankReference,
            Status = "Released"
        };

        await signalRNotifier.NotifyPayoutCompletedAsync(seller.Id, escrow.Id, anchorListing.Id, notificationPayload);

        // Xác minh SignalR đã bắn event PayoutCompleted tới các nhóm liên quan (seller, user, listing)
        mockGroup.Verify(p => p.SendCoreAsync(
            "PayoutCompleted",
            It.Is<object?[]>(args => args.Length == 1 && args[0] == notificationPayload),
            It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task EndToEnd_BundleOfThreeTickets_CompleteSettlementFlow_FromHoldToP2PPayout()
    {
        // Arrange - Kịch bản Combo 3 vé VIP
        await using var db = CreateInMemoryCoreDb();

        const int bundleTicketCount = 3;
        const decimal unitPrice = 1_500_000m;
        var (_, _, _, seller, buyer, bundleId, bundleListings) = SeedBundleListings(db, bundleTicketCount, unitPrice);

        // Act 1: Hold Bundle
        var holdHandler = new HoldListingForPurchaseCommandHandler(db, new FixedFeeCalculator(), new StubCurrentUserService(buyer.Id, buyer.Email));
        var holdResult = await holdHandler.Handle(new HoldListingForPurchaseCommand(bundleListings[0].Id), CancellationToken.None);
        Assert.True(holdResult.Success);

        // Act 2: Webhook payment & gRPC transfer 3 vé
        var mockVerificationService = new Mock<ITicketVerificationService>();
        var newCodes = new List<string>();

        for (var i = 0; i < bundleTicketCount; i++)
        {
            var code = $"NEW-VIP-COMBO-{i + 1:00}";
            newCodes.Add(code);
            mockVerificationService.Setup(v => v.TransferOwnershipByListingId(
                bundleListings[i].Id, buyer.Id, buyer.Email, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessTransferResponse(code));
        }

        var webhookHandler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerificationService.Object);
        // 3 vé * 1.500.000 = 4.500.000đ + 5% buyer fee (225.000đ) = 4.725.000đ
        const decimal totalBuyerPaid = 4_725_000m;
        await webhookHandler.Handle(new ProcessSePayWebhookCommand(CreateSePayPayload(holdResult.Data.PaymentReference!, totalBuyerPaid)), CancellationToken.None);

        var escrow = await db.EscrowTransactions.SingleAsync(e => e.BundleId == bundleId);
        Assert.Equal(EscrowStatus.Locked, escrow.Status);

        // Act 3: Quét vé 1 & 2 -> Escrow vẫn Locked (chống giải ngân sớm)
        var settler = new InMemoryEscrowPayoutSettler(db);
        var gateHandler = new ProcessTicketUsedWebhookCommandHandler(db, settler);

        await gateHandler.Handle(new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest { TicketCode = newCodes[0] }), CancellationToken.None);
        await gateHandler.Handle(new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest { TicketCode = newCodes[1] }), CancellationToken.None);

        db.ChangeTracker.Clear();
        var escrowStillLocked = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Locked, escrowStillLocked.Status);

        // Quét nốt vé 3 -> Kích hoạt giải ngân
        await gateHandler.Handle(new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest { TicketCode = newCodes[2] }), CancellationToken.None);

        db.ChangeTracker.Clear();
        var escrowReleasing = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Releasing, escrowReleasing.Status);

        // Act 4: Giải ngân qua Settlement API
        await using var settlementDb = await CreateSettlementDbAsync();
        var service = new SettlementPayoutService(settlementDb, new MockNapasPayoutGateway(), new StubCorePayoutReporter());
        var payoutResult = await service.AcceptAsync(new PayoutCommand
        {
            EscrowId = escrow.Id,
            SellerId = seller.Id,
            Amount = escrow.NetSellerPayout,
            IdempotencyKey = PayoutIdempotency.Key(escrow.Id, 0),
            AccountNumber = "0938434102",
            BankCode = "MB"
        });

        Assert.True(payoutResult.Accepted);
        Assert.Equal(TransferState.Succeeded, payoutResult.State);
        Assert.NotNull(payoutResult.BankReference);
    }

    [Fact]
    public async Task EndToEnd_BundleVietQrWebhook_IdempotentDuplicateReplay_PreventsDoubleGrpcTransfer()
    {
        // Arrange - Bắn webhook SePay trùng lặp cho gói combo
        await using var db = CreateInMemoryCoreDb();

        var (_, _, _, _, buyer, bundleId, bundleListings) = SeedBundleListings(db, 2, 500_000m);
        var holdHandler = new HoldListingForPurchaseCommandHandler(db, new FixedFeeCalculator(), new StubCurrentUserService(buyer.Id, buyer.Email));
        var holdResult = await holdHandler.Handle(new HoldListingForPurchaseCommand(bundleListings[0].Id), CancellationToken.None);

        var mockVerificationService = new Mock<ITicketVerificationService>();
        mockVerificationService.Setup(v => v.TransferOwnershipByListingId(
            It.IsAny<Guid>(), buyer.Id, buyer.Email, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateSuccessTransferResponse("NEW-CODE-IDEMP"));

        var webhookHandler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerificationService.Object);
        var payload = CreateSePayPayload(holdResult.Data.PaymentReference!, 1_050_000m);

        // Act 1: Lần gọi đầu tiên -> gRPC được gọi 2 lần cho 2 vé trong bundle
        var firstCall = await webhookHandler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(firstCall.Success);
        Assert.False(firstCall.Data.IsIdempotentDuplicate);

        // Act 2: Webhook bắn trùng lặp lần 2
        var secondCall = await webhookHandler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert - Idempotent an toàn: trả về thành công cũ, TUYỆT ĐỐI KHÔNG gọi thêm gRPC
        Assert.True(secondCall.Success);
        Assert.True(secondCall.Data.IsIdempotentDuplicate);

        // Tổng số lần gọi gRPC xuyên suốt vẫn duy nhất là 2 lần (không bị nhân đôi thành 4 lần)
        mockVerificationService.Verify(v => v.TransferOwnershipByListingId(
            It.IsAny<Guid>(), buyer.Id, buyer.Email, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Số Escrow trong CSDL chỉ duy nhất 1
        Assert.Equal(1, await db.EscrowTransactions.CountAsync(e => e.BundleId == bundleId));
    }

    [Fact]
    public async Task EndToEnd_BundleGrpcFailure_CompensatesSagaAndRollbacksAllListings()
    {
        // Arrange - Giả lập lỗi gRPC khi sang tên vé thứ 2 trong bundle 2 vé
        await using var db = CreateInMemoryCoreDb();

        var (_, _, _, _, buyer, bundleId, bundleListings) = SeedBundleListings(db, 2, 800_000m);
        var holdHandler = new HoldListingForPurchaseCommandHandler(db, new FixedFeeCalculator(), new StubCurrentUserService(buyer.Id, buyer.Email));
        var holdResult = await holdHandler.Handle(new HoldListingForPurchaseCommand(bundleListings[0].Id), CancellationToken.None);

        var mockVerificationService = new Mock<ITicketVerificationService>();

        // Vé 1 sang tên thành công
        mockVerificationService.Setup(v => v.TransferOwnershipByListingId(
            bundleListings[0].Id, buyer.Id, buyer.Email, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateSuccessTransferResponse("NEW-1"));

        // Vé 2 bị lỗi RpcException từ Ban tổ chức
        mockVerificationService.Setup(v => v.TransferOwnershipByListingId(
            bundleListings[1].Id, buyer.Id, buyer.Email, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "BTC Organizer Server Down")));

        var webhookHandler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerificationService.Object);
        var payload = CreateSePayPayload(holdResult.Data.PaymentReference!, 1_680_000m);

        // Act - Webhook xử lý và tự động kích hoạt Saga Compensation khi gRPC ném lỗi
        var webhookResponse = await webhookHandler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        Assert.NotNull(webhookResponse);
        Assert.True(webhookResponse.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), webhookResponse.Data.EscrowStatus);
        Assert.Equal(nameof(ListingStatus.Verified), webhookResponse.Data.ListingStatus);

        // Xác minh Saga Compensation rollback an toàn 100%:
        db.ChangeTracker.Clear();
        var rolledBackListings = await db.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.All(rolledBackListings, l => Assert.Equal(ListingStatus.Verified, l.ListingStatus));

        var escrow = await db.EscrowTransactions.SingleAsync(e => e.BundleId == bundleId);
        Assert.Equal(EscrowStatus.RefundQueued, escrow.Status);
        Assert.Null(escrow.NewTicketCode);
    }

    [Fact]
    public async Task EndToEnd_SettlementPayout_IdempotentDuplicatePayoutCommand_ReturnsExistingBankReference()
    {
        // Arrange - Lệnh giải ngân trùng lặp ở tầng Settlement API
        await using var db = await CreateSettlementDbAsync();
        var mockGateway = new MockNapasPayoutGateway();
        var service = new SettlementPayoutService(db, mockGateway, new StubCorePayoutReporter());

        var escrowId = Guid.NewGuid();
        var command = new PayoutCommand
        {
            EscrowId = escrowId,
            SellerId = Guid.NewGuid(),
            Amount = 2_500_000m,
            RetryCount = 0,
            IdempotencyKey = PayoutIdempotency.Key(escrowId, 0),
            BankCode = "MB",
            AccountNumber = "0938434102",
            AccountName = "NGUYEN VAN SELLER"
        };

        // Act 1: Lần đầu tiên -> gọi gateway
        var first = await service.AcceptAsync(command, CancellationToken.None);
        Assert.True(first.Accepted);
        Assert.True(first.GatewayCalled);
        Assert.NotNull(first.BankReference);

        // Act 2: Bắn lại lệnh giải ngân trùng lặp (Duplicate Dispatch)
        var second = await service.AcceptAsync(command, CancellationToken.None);

        // Assert - Trả về kết quả cũ, KHÔNG gọi lại gateway, KHÔNG tạo thêm đơn chuyển tiền
        Assert.True(second.Accepted);
        Assert.False(second.GatewayCalled);
        Assert.Equal(first.BankReference, second.BankReference);
        Assert.Equal(1, await db.TransferOrders.CountAsync(t => t.IdempotencyKey == command.IdempotencyKey));
    }

    #endregion
}
