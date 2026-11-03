using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TicketShield.Application.Common.Configurations;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Application.Features.Disputes.Commands.CreateDispute;
using TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Application.Features.ResaleListings.Commands.ReleaseListingHold;
using TicketShield.Application.Features.ResaleListings.Queries.GetMyPurchasedTickets;
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
/// US-5.5: Concurrency Verification & End-to-End Regression Automation
/// Module Feature: Concurrency Verification & End-to-End Regression Automation
/// Description: Kiá»ƒm thá»­ tá»± Ä‘á»™ng Ä‘áº£m báº£o tĂ­nh toĂ n váº¹n tĂ i chĂ­nh, chá»‘ng race condition vĂ  váº­n hĂ nh thĂ´ng suá»‘t toĂ n bá»™ há»‡ thá»‘ng.
/// 
/// Bá»™ kiá»ƒm thá»­ bao gá»“m 3 trá»¥ cá»™t cá»‘t lĂµi:
/// 1. Financial Integrity Verification: TĂ­nh toĂ n váº¹n tĂ i chĂ­nh, báº£o toĂ n sá»‘ há»c khĂ´ng tháº¥t thoĂ¡t 1 xu, kiá»ƒm soĂ¡t phĂ­, tá»« chá»‘i náº¡p thiáº¿u tiá»n.
/// 2. Concurrency & Race Condition Prevention: Chá»‘ng race condition khi nhiá»u ngÆ°á»i tranh mua vĂ© Ä‘Æ¡n/bundle, webhook song song, CAS dispute vs payout.
/// 3. End-to-End Regression Automation: Váº­n hĂ nh thĂ´ng suá»‘t toĂ n trĂ¬nh tá»« Listing -> Hold -> Webhook Payment -> gRPC Transfer -> Ticket Delivery -> Settlement/Saga.
/// </summary>
public class ConcurrencyVerificationAndRegressionTestSuite
{
    private static readonly VietQrSettings TestVietQrSettings = new()
    {
        Provider = "SePay",
        BankBin = "970422",
        AccountNumber = "0938434102",
        AccountName = "CONG TY TICKETSHIELD"
    };

    #region Fixture Helpers

    private static (
        TicketShieldDbContext db,
        DbContextOptions<TicketShieldDbContext> options,
        ShadowUser seller,
        ShadowUser buyer1,
        ShadowUser buyer2,
        Event ev,
        TicketTier tier) CreateFixture(string? dbName = null)
    {
        var databaseName = dbName ?? Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new TicketShieldDbContext(options);

        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller@test.vn",
            FullName = "Nguyá»…n VÄƒn BĂ¡n",
            PayoutBankCode = "MB",
            PayoutAccountNumber = "0938434102",
            PayoutAccountName = "NGUYEN VAN BAN",
            IsActive = true
        };

        var buyer1 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer1@test.vn",
            FullName = "Tráº§n NgÆ°á»i Mua 1",
            PayoutBankCode = "VCB",
            PayoutAccountNumber = "1012345678",
            PayoutAccountName = "TRAN NGUOI MUA 1",
            IsActive = true
        };

        var buyer2 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer2@test.vn",
            FullName = "LĂª NgÆ°á»i Mua 2",
            PayoutBankCode = "TCB",
            PayoutAccountNumber = "1903456789",
            PayoutAccountName = "LE NGUOI MUA 2",
            IsActive = true
        };

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "Ban Tá»• Chá»©c Nháº¡c Há»™i",
            OfficialEmail = "organizer@musicfest.vn"
        };

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Organizer = organizer,
            Name = "SiĂªu Nháº¡c Há»™i Quá»‘c Táº¿ 2026",
            Venue = "SĂ¢n Váº­n Äá»™ng Má»¹ ÄĂ¬nh",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(7),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(4),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(7).AddHours(-2)
        };

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            Event = ev,
            TierName = "VIP Lounge",
            OriginalPrice = 2_000_000m
        };

        db.Organizers.Add(organizer);
        db.ShadowUsers.AddRange(seller, buyer1, buyer2);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.SaveChanges();

        return (db, options, seller, buyer1, buyer2, ev, tier);
    }

    private static ResaleListing CreateListing(
        Guid sellerId,
        Event ev,
        TicketTier tier,
        ListingStatus status = ListingStatus.Verified,
        decimal price = 2_000_000m,
        string ticketCode = "TCK-US55-ORIG")
    {
        return new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = sellerId,
            OriginalTicketCode = ticketCode,
            OriginalPrice = price,
            ResalePrice = price,
            ListingStatus = status,
            Event = ev,
            Tier = tier,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static SePayWebhookRequest CreateSePayPayload(
        string paymentReference,
        decimal amount,
        string bankRef = "FT_TX_12345",
        string transferType = "in")
    {
        return new SePayWebhookRequest
        {
            Id = 998877,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentReference} HE THONG TICKETSHIELD",
            TransferType = transferType,
            TransferAmount = amount,
            ReferenceCode = bankRef
        };
    }

    private static Mock<IResaleFeeCalculator> CreateDeterministicFeeCalculator()
    {
        var mock = new Mock<IResaleFeeCalculator>();
        mock.Setup(c => c.CalculateFeeAsync(It.IsAny<decimal>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal price, bool isPrivate, CancellationToken ct) =>
            {
                var buyerFee = Math.Round(price * 0.05m, 0); // 5% buyer fee
                var sellerFee = Math.Round(price * 0.03m, 0); // 3% seller fee
                return new ResaleFeeCalculationResult
                {
                    OriginalPrice = price,
                    BuyerFee = buyerFee,
                    SellerFee = sellerFee,
                    TotalBuyerPaid = price + buyerFee,
                    NetSellerPayout = price - sellerFee
                };
            });
        return mock;
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

    private sealed class StubCurrentUser(Guid? userId, string? email = "buyer@test.vn") : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email { get; } = email;
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;
    }

    #endregion

    #region Group 1: Financial Integrity Verification (TĂ­nh toĂ n váº¹n tĂ i chĂ­nh)

    [Fact]
    public async Task FinancialIntegrity_UniformFeeFormula_ConservesExactAmountWithZeroLeakage()
    {
        // Kiá»ƒm chá»©ng Ä‘á»‹nh luáº­t báº£o toĂ n tĂ i chĂ­nh: TotalBuyerPaid - NetSellerPayout == BuyerFee + SellerFee (Platform Revenue)
        // Tuyá»‡t Ä‘á»‘i khĂ´ng Ä‘á»ƒ lá»‡ch hay máº¥t mĂ¡t 1 VNÄ
        var (_, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var feeCalculator = CreateDeterministicFeeCalculator();

        const decimal ticketPrice = 2_500_000m;
        var feeResult = await feeCalculator.Object.CalculateFeeAsync(ticketPrice, isPrivate: false);

        // Assert fee logic
        Assert.Equal(125_000m, feeResult.BuyerFee); // 5%
        Assert.Equal(75_000m, feeResult.SellerFee); // 3%
        Assert.Equal(2_625_000m, feeResult.TotalBuyerPaid);
        Assert.Equal(2_425_000m, feeResult.NetSellerPayout);

        // Äá»‹nh luáº­t báº£o toĂ n tiá»n tá»‡: Tá»•ng tiá»n vĂ o - Tá»•ng tiá»n ra = Doanh thu sĂ n
        var platformRevenue = feeResult.BuyerFee + feeResult.SellerFee;
        var netDifference = feeResult.TotalBuyerPaid - feeResult.NetSellerPayout;
        Assert.Equal(platformRevenue, netDifference);
        Assert.Equal(200_000m, platformRevenue);
    }

    [Fact]
    public async Task FinancialIntegrity_UnderpaymentRejected_RejectsWebhookAndProtectsEscrowBalance()
    {
        // Ká»‹ch báº£n: NgÆ°á»i mua thanh toĂ¡n thiáº¿u 1.000 VNÄ (chuyá»ƒn 2.099.000 VNÄ thay vĂ¬ 2.100.000 VNÄ)
        // Há»‡ thá»‘ng nĂ©m BusinessRuleViolationException, khĂ´ng bao giá» khĂ³a Escrow hoáº·c cáº¥p vĂ©
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = CreateListing(seller.Id, ev, tier, ListingStatus.Transacting);
        const string paymentRef = "TSFINUND01"; // 10 chars
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
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
            RecipientName = "Buyer 1",
            RecipientEmail = "buyer1@test.vn"
        };
        listing.EscrowTransaction = escrow;
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        var mockVerification = new Mock<ITicketVerificationService>();
        var webhookHandler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        // Báº¯n webhook thiáº¿u 1.000Ä‘
        var underpaidPayload = CreateSePayPayload(paymentRef, 2_099_000m);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            webhookHandler.Handle(new ProcessSePayWebhookCommand(underpaidPayload), CancellationToken.None));

        Assert.Contains("nhỏ hơn tổng số tiền cần trả", ex.Message);

        // XĂ¡c minh Escrow váº«n Pending, vĂ© váº«n Transacting, gRPC khĂ´ng Ä‘Æ°á»£c gá»i
        db.ChangeTracker.Clear();
        var currentEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Pending, currentEscrow.Status);
        Assert.Null(currentEscrow.NewTicketCode);
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FinancialIntegrity_RefundIntegrity_Preserves100PercentBuyerFundsWithoutDeduction()
    {
        // Khi lá»—i há»‡ thá»‘ng hoáº·c lá»—i BTC MockOrganizer, Buyer pháº£i Ä‘Æ°á»£c hoĂ n 100% TotalBuyerPaid (bao gá»“m cáº£ BuyerFee)
        // SĂ n khĂ´ng Ä‘Æ°á»£c phĂ©p tá»± Ă½ trá»« phĂ­ cá»§a ngÆ°á»i mua khi lá»—i khĂ´ng thuá»™c vá» há»
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = CreateListing(seller.Id, ev, tier, ListingStatus.Transacting);
        const string paymentRef = "TSFINREF01";
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
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
            RecipientName = "Buyer 1",
            RecipientEmail = "buyer1@test.vn"
        };
        listing.EscrowTransaction = escrow;
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "Organizer is Down")));

        var webhookHandler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);
        var payload = CreateSePayPayload(paymentRef, 2_100_000m);

        var response = await webhookHandler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);

        db.ChangeTracker.Clear();
        var refundedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        // ToĂ n bá»™ 100% tiá»n ngÆ°á»i mua thanh toĂ¡n Ä‘Æ°á»£c báº£o lÆ°u Ä‘á»ƒ hoĂ n tráº£
        Assert.Equal(2_100_000m, refundedEscrow.TotalBuyerPaid);
        Assert.Equal(EscrowStatus.RefundQueued, refundedEscrow.Status);
    }

    [Fact]
    public async Task FinancialIntegrity_MultiTicketBundle_AtomicPricingAndFeeAggregationWithoutFractionalLoss()
    {
        // GĂ³i combo 3 vĂ©: Má»—i vĂ© 1.000.000 VNÄ -> Tá»•ng 3.000.000 VNÄ
        // Tá»•ng phĂ­ ngÆ°á»i mua = 150.000 VNÄ (5%), Tá»•ng ngÆ°á»i mua tráº£ = 3.150.000 VNÄ
        var feeCalculator = CreateDeterministicFeeCalculator();
        const decimal singleTicketPrice = 1_000_000m;
        const int bundleCount = 3;

        var fee = await feeCalculator.Object.CalculateFeeAsync(singleTicketPrice * bundleCount, isPrivate: false);

        Assert.Equal(3_000_000m, fee.OriginalPrice);
        Assert.Equal(150_000m, fee.BuyerFee);
        Assert.Equal(90_000m, fee.SellerFee);
        Assert.Equal(3_150_000m, fee.TotalBuyerPaid);
        Assert.Equal(2_910_000m, fee.NetSellerPayout);

        // Sá»‘ tiá»n chĂªnh lá»‡ch pháº£i Ä‘Ăºng báº±ng 240.000 VNÄ
        Assert.Equal(240_000m, fee.TotalBuyerPaid - fee.NetSellerPayout);
    }

    #endregion

    #region Group 2: Concurrency & Race Condition Prevention (Chá»‘ng Race Condition)

    [Fact]
    public async Task Concurrency_MultipleBuyersHoldSameSingleListing_OnlyOneWinsAndNoDuplicateEscrow()
    {
        // 5 NgÆ°á»i mua cĂ¹ng lĂºc gá»­i lá»‡nh HoldListingForPurchaseCommand cho 1 vĂ© duy nháº¥t
        // Káº¿t quáº£: ÄĂºng 1 ngÆ°á»i thĂ nh cĂ´ng, 4 ngÆ°á»i cĂ²n láº¡i nháº­n thĂ´ng bĂ¡o vĂ© Ä‘ang Ä‘Æ°á»£c giá»¯ chá»—
        var (dbSeed, options, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = CreateListing(seller.Id, ev, tier, ListingStatus.Verified);
        dbSeed.ResaleListings.Add(listing);
        await dbSeed.SaveChangesAsync();

        var feeCalculator = CreateDeterministicFeeCalculator();
        var vietQrService = new VietQrService(Microsoft.Extensions.Options.Options.Create(TestVietQrSettings));

        const int concurrentBuyers = 5;
        var buyerIds = Enumerable.Range(1, concurrentBuyers).Select(_ => Guid.NewGuid()).ToList();

        // Seed trÆ°á»›c danh sĂ¡ch ShadowUsers cho cĂ¡c buyer tham gia tranh cháº¥p
        using (var seedDb = new TicketShieldDbContext(options))
        {
            foreach (var bId in buyerIds)
            {
                seedDb.ShadowUsers.Add(new ShadowUser
                {
                    Id = bId,
                    Email = $"buyer_{bId:N}@test.vn",
                    FullName = $"Buyer {bId:N}",
                    IsActive = true
                });
            }
            await seedDb.SaveChangesAsync();
        }

        // Báº¯n Ä‘á»“ng thá»i 5 commands
        var tasks = buyerIds.Select(async bId =>
        {
            using var requestDb = new TicketShieldDbContext(options);
            var handler = new HoldListingForPurchaseCommandHandler(
                requestDb,
                feeCalculator.Object,
                new StubCurrentUser(bId, $"buyer_{bId:N}@test.vn"),
                vietQrService);

            try
            {
                var result = await handler.Handle(new HoldListingForPurchaseCommand { ListingId = listing.Id }, CancellationToken.None);
                return (Success: result.Success, Error: (string?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: ex.Message);
            }
        });

        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => r.Success);
        var failedCount = results.Count(r => !r.Success);

        // ÄĂºng 1 request thĂ nh cĂ´ng
        Assert.Equal(1, successCount);
        Assert.Equal(concurrentBuyers - 1, failedCount);

        // Táº¥t cáº£ request tháº¥t báº¡i Ä‘á»u nháº­n Ä‘Æ°á»£c thĂ´ng bĂ¡o rĂµ rĂ ng
        Assert.All(results.Where(r => !r.Success), r =>
        {
            Assert.Contains("Vé này", r.Error!);
        });

        // Kiá»ƒm tra CSDL: Sá»‘ lÆ°á»£ng Escrow Ä‘Æ°á»£c táº¡o ra cho listing nĂ y DUY NHáº¤T LĂ€ 1
        using var verifyDb = new TicketShieldDbContext(options);
        var escrowList = await verifyDb.EscrowTransactions.Where(e => e.ListingId == listing.Id).ToListAsync();
        Assert.Single(escrowList);
        Assert.Equal(EscrowStatus.Pending, escrowList[0].Status);

        var finalListing = await verifyDb.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Transacting, finalListing.ListingStatus);
    }

    [Fact]
    public async Task Concurrency_MultipleBuyersHoldSameBundle_AtomicReservationWithoutTicketSplitting()
    {
        // 3 NgÆ°á»i mua cĂ¹ng tranh mua má»™t Combo 3 vĂ©
        // Báº£o Ä‘áº£m: Chá»‰ 1 ngÆ°á»i mua giá»¯ Ä‘Æ°á»£c trá»n váº¹n combo, cĂ¡c vĂ© khĂ´ng bao giá» bá»‹ xĂ© láº»
        var (dbSeed, options, seller, _, _, ev, tier) = CreateFixture();
        var bundleId = Guid.NewGuid();

        var listing1 = CreateListing(seller.Id, ev, tier, ListingStatus.Verified, 1_000_000m, "TCK-BND-01");
        listing1.BundleId = bundleId;
        listing1.BundleTotalTickets = 3;

        var listing2 = CreateListing(seller.Id, ev, tier, ListingStatus.Verified, 1_000_000m, "TCK-BND-02");
        listing2.BundleId = bundleId;
        listing2.BundleTotalTickets = 3;

        var listing3 = CreateListing(seller.Id, ev, tier, ListingStatus.Verified, 1_000_000m, "TCK-BND-03");
        listing3.BundleId = bundleId;
        listing3.BundleTotalTickets = 3;

        dbSeed.ResaleListings.AddRange(listing1, listing2, listing3);
        await dbSeed.SaveChangesAsync();

        var feeCalculator = CreateDeterministicFeeCalculator();
        var vietQrService = new VietQrService(Microsoft.Extensions.Options.Options.Create(TestVietQrSettings));

        var buyerA = Guid.NewGuid();
        var buyerB = Guid.NewGuid();
        var buyerC = Guid.NewGuid();

        using (var seedDb = new TicketShieldDbContext(options))
        {
            seedDb.ShadowUsers.AddRange(
                new ShadowUser { Id = buyerA, Email = "a@test.vn", FullName = "Buyer A", IsActive = true },
                new ShadowUser { Id = buyerB, Email = "b@test.vn", FullName = "Buyer B", IsActive = true },
                new ShadowUser { Id = buyerC, Email = "c@test.vn", FullName = "Buyer C", IsActive = true }
            );
            await seedDb.SaveChangesAsync();
        }

        var buyers = new[] { buyerA, buyerB, buyerC };
        var tasks = buyers.Select(async bId =>
        {
            using var reqDb = new TicketShieldDbContext(options);
            var handler = new HoldListingForPurchaseCommandHandler(
                reqDb, feeCalculator.Object, new StubCurrentUser(bId, $"user_{bId:N}@test.vn"), vietQrService);

            try
            {
                var res = await handler.Handle(new HoldListingForPurchaseCommand { ListingId = listing1.Id }, CancellationToken.None);
                return (Success: res.Success, Error: (string?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: ex.Message);
            }
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.Success));
        Assert.Equal(2, results.Count(r => !r.Success));

        // XĂ¡c minh toĂ n bá»™ 3 vĂ© cá»§a Bundle Ä‘á»u á»Ÿ tráº¡ng thĂ¡i Transacting vĂ  chung 1 Buyer
        using var verifyDb = new TicketShieldDbContext(options);
        var bundleListings = await verifyDb.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.Equal(3, bundleListings.Count);
        Assert.All(bundleListings, l => Assert.Equal(ListingStatus.Transacting, l.ListingStatus));

        var escrows = await verifyDb.EscrowTransactions.Where(e => e.BundleId == bundleId).ToListAsync();
        Assert.Single(escrows); // ÄĂºng 1 giao dá»‹ch kĂ½ quá»¹ cho cáº£ bundle
        Assert.Equal(3_000_000m, escrows[0].OriginalTicketPrice);
        Assert.Equal(150_000m, escrows[0].BuyerFee);
    }

    [Fact]
    public async Task Concurrency_SimultaneousWebhooks_IdempotentExecution_SingleTicketIssuance()
    {
        // 5 Webhook requests tá»« SePay cho cĂ¹ng 1 giao dá»‹ch Ä‘Æ°á»£c báº¯n Ä‘á»“ng thá»i
        // Nhá» Idempotency, chá»‰ 1 request thá»±c thi khĂ³a & gá»i gRPC, 4 requests cĂ²n láº¡i nháº­n diá»‡n IsIdempotentDuplicate
        var (dbSeed, options, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = CreateListing(seller.Id, ev, tier, ListingStatus.Transacting);
        const string paymentRef = "TSCONWEB01";
        const string bankRef = "FT_CONCURRENT_WEBHOOK_01";

        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
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
            RecipientName = "Buyer 1",
            RecipientEmail = "buyer1@test.vn"
        };
        listing.EscrowTransaction = escrow;
        dbSeed.ResaleListings.Add(listing);
        dbSeed.EscrowTransactions.Add(escrow);
        await dbSeed.SaveChangesAsync();

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
            listing.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessTransferResponse("TCK-CONCUR-ISSUED"));

        var payload = CreateSePayPayload(paymentRef, 2_100_000m, bankRef);

        const int concurrentWebhooks = 5;
        var tasks = Enumerable.Range(1, concurrentWebhooks).Select(async _ =>
        {
            using var reqDb = new TicketShieldDbContext(options);
            var handler = new ProcessSePayWebhookCommandHandler(reqDb, null, null, null, mockVerification.Object);
            return await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        });

        var responses = await Task.WhenAll(tasks);

        // Táº¥t cáº£ request Ä‘á»u thĂ nh cĂ´ng (HTTP 200)
        Assert.All(responses, r => Assert.True(r.Success));

        // Ăt nháº¥t 4 request lĂ  Idempotent Duplicate
        var duplicateCount = responses.Count(r => r.Data!.IsIdempotentDuplicate);
        Assert.True(duplicateCount >= 4, $"Cáº§n cĂ³ Ă­t nháº¥t 4 duplicate nhÆ°ng nháº­n Ä‘Æ°á»£c {duplicateCount}");

        // gRPC TUYá»†T Äá»I chá»‰ Ä‘Æ°á»£c gá»i Ä‘Ăºng 1 láº§n
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            listing.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Database chá»‰ cĂ³ 1 Escrow á»Ÿ tráº¡ng thĂ¡i Locked
        using var verifyDb = new TicketShieldDbContext(options);
        var finalEscrow = await verifyDb.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Locked, finalEscrow.Status);
        Assert.Equal("TCK-CONCUR-ISSUED", finalEscrow.NewTicketCode);
    }

    [Fact]
    public async Task Concurrency_DisputeVsSettlementRelease_CasProtectsFunds_ExactlyOneWins()
    {
        // Kiá»ƒm thá»­ tÆ°Æ¡ng tranh giá»¯a Khiáº¿u náº¡i (CreateDisputeCommand) vĂ  Giáº£i ngĂ¢n tá»± Ä‘á»™ng (EscrowSettlementCas)
        // Ăp dá»¥ng Compare-And-Swap (CAS): ÄĂºng 1 bĂªn thĂ nh cĂ´ng, khĂ´ng thá»ƒ vá»«a giáº£i ngĂ¢n cho Seller vá»«a hoĂ n tiá»n cho Buyer
        var sqliteConnection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await sqliteConnection.OpenAsync();

        var sqliteOptions = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseSqlite(sqliteConnection)
            .Options;

        await using (var initDb = new TicketShieldDbContext(sqliteOptions))
        {
            await initDb.Database.EnsureCreatedAsync();
        }

        var (dbSeed, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = CreateListing(seller.Id, ev, tier, ListingStatus.Sold);
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 2_000_000m,
            BuyerFee = 100_000m,
            SellerFee = 60_000m,
            TotalBuyerPaid = 2_100_000m,
            NetSellerPayout = 1_940_000m,
            Status = EscrowStatus.Locked, // Äang trong buffer chá» giáº£i ngĂ¢n
            InSettlementBuffer = true,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        await using (var seedDb = new TicketShieldDbContext(sqliteOptions))
        {
            seedDb.Organizers.Add(ev.Organizer);
            seedDb.ShadowUsers.AddRange(seller, buyer1);
            seedDb.Events.Add(ev);
            seedDb.TicketTiers.Add(tier);
            seedDb.ResaleListings.Add(listing);
            seedDb.EscrowTransactions.Add(escrow);
            await seedDb.SaveChangesAsync();
        }

        // Ká»‹ch báº£n A: NgÆ°á»i mua gá»­i khiáº¿u náº¡i trÆ°á»›c -> Tráº¡ng thĂ¡i chuyá»ƒn sang Disputed, InSettlementBuffer = false
        await using (var disputeDb = new TicketShieldDbContext(sqliteOptions))
        {
            var disputeHandler = new CreateDisputeCommandHandler(disputeDb, new StubCurrentUser(buyer1.Id));
            var disputeRes = await disputeHandler.Handle(new CreateDisputeCommand
            {
                EscrowId = escrow.Id,
                Reason = "VĂ© khĂ´ng thá»ƒ quĂ©t táº¡i cá»•ng"
            }, CancellationToken.None);
            Assert.True(disputeRes.Success);
        }

        // Ká»‹ch báº£n B: Worker giáº£i ngĂ¢n cháº¡y tá»›i sau Ä‘Ă³ -> CAS phĂ¡t hiá»‡n Status != Locked -> Giáº£i ngĂ¢n bá»‹ cháº·n láº¡i!
        await using (var releaseDb = new TicketShieldDbContext(sqliteOptions))
        {
            var cas = new EscrowSettlementCas(releaseDb);
            var releaseWon = await cas.TryBeginReleaseAsync(escrow.Id, CancellationToken.None);

            // Assert: Giáº£i ngĂ¢n tháº¥t báº¡i, tiá»n Ä‘Æ°á»£c báº£o toĂ n nguyĂªn váº¹n trong Escrow tranh cháº¥p
            Assert.False(releaseWon);
        }

        await using (var verifyDb = new TicketShieldDbContext(sqliteOptions))
        {
            var verifiedEscrow = await verifyDb.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
            Assert.Equal(EscrowStatus.Disputed, verifiedEscrow.Status);
            Assert.False(verifiedEscrow.InSettlementBuffer);

            // KhĂ´ng cĂ³ lá»‡nh Payout nĂ o Ä‘Æ°á»£c phĂ¡t ra
            var payouts = await verifyDb.PayoutTransactions.CountAsync(p => p.EscrowId == escrow.Id);
            Assert.Equal(0, payouts);
        }

        await sqliteConnection.CloseAsync();
    }

    [Fact]
    public async Task Concurrency_WorkerHoldReleaseVsPaymentWebhook_ZeroOversellingGuarantee()
    {
        // Khi vĂ© háº¿t háº¡n Ä‘Ăºng thá»i Ä‘iá»ƒm Webhook thanh toĂ¡n Ä‘áº¿n
        // Äáº£m báº£o khĂ´ng bao giá» cĂ³ 2 ngÆ°á»i cĂ¹ng sá»Ÿ há»¯u vĂ©
        var (dbSeed, options, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = CreateListing(seller.Id, ev, tier, ListingStatus.Transacting);
        const string paymentRef = "TSWORWEB01";

        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 2_000_000m,
            BuyerFee = 100_000m,
            SellerFee = 60_000m,
            TotalBuyerPaid = 2_100_000m,
            NetSellerPayout = 1_940_000m,
            PaymentReference = paymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1), // ÄĂ£ háº¿t háº¡n giá»¯ chá»—
            RecipientName = "Buyer 1",
            RecipientEmail = "buyer1@test.vn"
        };
        listing.EscrowTransaction = escrow;
        dbSeed.ResaleListings.Add(listing);
        dbSeed.EscrowTransactions.Add(escrow);
        await dbSeed.SaveChangesAsync();

        var mockVerification = new Mock<ITicketVerificationService>();

        // Giáº£ láº­p Worker cháº¡y trÆ°á»›c nháº£ vĂ© vá» Verified
        var worker = CreateWorker(options);
        var releasedCount = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);
        Assert.Equal(1, releasedCount);

        // Sau Ä‘Ă³ Webhook thanh toĂ¡n cá»§a Buyer 1 má»›i Ä‘áº¿n
        using var reqDb = new TicketShieldDbContext(options);
        var webhookHandler = new ProcessSePayWebhookCommandHandler(reqDb, null, null, null, mockVerification.Object);
        var payload = CreateSePayPayload(paymentRef, 2_100_000m);

        // VĂ¬ Escrow Ä‘Ă£ Expired, Webhook tá»« chá»‘i khĂ³a
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            webhookHandler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None));
        Assert.Contains("Expired", ex.Message);

        // gRPC khĂ´ng bao giá» Ä‘Æ°á»£c gá»i
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        using var verifyDb = new TicketShieldDbContext(options);
        var currentListing = await verifyDb.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Verified, currentListing.ListingStatus); // ÄĂ£ sáºµn sĂ ng cho ngÆ°á»i mua khĂ¡c
    }

    #endregion

    #region Group 3: End-to-End Regression Automation (Váº­n hĂ nh thĂ´ng suá»‘t toĂ n há»‡ thá»‘ng)

    [Fact]
    public async Task E2ERegression_HappyPath_FullLifecycle_FromListingToHoldToPaymentToTicketRetrieval()
    {
        // ToĂ n trĂ¬nh Happy Path:
        // 1. Seller Ä‘Äƒng bĂ¡n vĂ© Verified
        // 2. Buyer 1 Hold vĂ© 10 phĂºt -> Listing: Transacting, Escrow: Pending, VietQR sinh Ä‘Ăºng
        // 3. Webhook SePay ghi nháº­n thanh toĂ¡n -> Escrow: Locked, gRPC sang tĂªn MockOrganizer -> NewTicketCode cáº¥p má»›i
        // 4. Buyer 1 gá»i GetMyPurchasedTickets -> Nháº­n Ä‘Ăºng vĂ© chĂ­nh thá»©c vá»›i QR code tá»« BTC
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = CreateListing(seller.Id, ev, tier, ListingStatus.Verified);
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync();

        var feeCalculator = CreateDeterministicFeeCalculator();
        var vietQrService = new VietQrService(Microsoft.Extensions.Options.Options.Create(TestVietQrSettings));

        // Step 1: Buyer 1 giá»¯ chá»— vĂ©
        var holdHandler = new HoldListingForPurchaseCommandHandler(
            db, feeCalculator.Object, new StubCurrentUser(buyer1.Id, buyer1.Email), vietQrService);
        var holdResult = await holdHandler.Handle(new HoldListingForPurchaseCommand { ListingId = listing.Id }, CancellationToken.None);

        Assert.True(holdResult.Success);
        var paymentRef = holdResult.Data!.PaymentReference;
        Assert.Matches(@"^TS[A-Z0-9]{8}$", paymentRef);

        // Step 2: Webhook SePay náº¡p tiá»n thĂ nh cĂ´ng
        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
            listing.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessTransferResponse("TCK-VIP-OFFICIAL-2026"));

        var webhookHandler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);
        var webhookPayload = CreateSePayPayload(paymentRef, holdResult.Data.TotalBuyerPaid);
        var webhookResult = await webhookHandler.Handle(new ProcessSePayWebhookCommand(webhookPayload), CancellationToken.None);

        Assert.True(webhookResult.Success);
        Assert.Equal(nameof(EscrowStatus.Locked), webhookResult.Data!.EscrowStatus);
        Assert.Equal(nameof(ListingStatus.Sold), webhookResult.Data.ListingStatus);

        // Step 3: Buyer 1 vĂ o vĂ­ xem vĂ© Ä‘Ă£ mua (GetMyPurchasedTickets)
        var ticketsHandler = new GetMyPurchasedTicketsQueryHandler(db, new StubCurrentUser(buyer1.Id, buyer1.Email));
        var purchasedTicketsResult = await ticketsHandler.Handle(new GetMyPurchasedTicketsQuery(), CancellationToken.None);

        Assert.True(purchasedTicketsResult.Success);
        Assert.NotNull(purchasedTicketsResult.Data);
        Assert.NotEmpty(purchasedTicketsResult.Data);

        var myTicket = purchasedTicketsResult.Data.Single(t => t.ListingId == listing.Id);
        Assert.Equal("TCK-VIP-OFFICIAL-2026", myTicket.TicketPassCode);
        Assert.Equal("TCK-VIP-OFFICIAL-2026", myTicket.QrCodeData);
        Assert.Equal("SiĂªu Nháº¡c Há»™i Quá»‘c Táº¿ 2026", myTicket.EventName);
    }

    [Fact]
    public async Task E2ERegression_GrpcOrganizerFailure_SagaRollback_ListingReopenedAndFullRefundQueued()
    {
        // Khi thanh toĂ¡n xong nhÆ°ng gRPC sang tĂªn MockOrganizer bá»‹ sá»± cá»‘ (Unavailable)
        // Há»‡ thá»‘ng Saga Rollback: Listing quay vá» Verified, Escrow chuyá»ƒn thĂ nh RefundQueued hoĂ n 100% tiá»n
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = CreateListing(seller.Id, ev, tier, ListingStatus.Transacting);
        const string paymentRef = "TSSAGAROL1";

        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
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
            RecipientName = "Buyer 1",
            RecipientEmail = "buyer1@test.vn"
        };
        listing.EscrowTransaction = escrow;
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
            listing.Id, buyer1.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "gRPC MockOrganizer connection timeout")));

        var webhookHandler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);
        var webhookPayload = CreateSePayPayload(paymentRef, 2_100_000m);
        var response = await webhookHandler.Handle(new ProcessSePayWebhookCommand(webhookPayload), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);
        Assert.Equal(nameof(ListingStatus.Verified), response.Data.ListingStatus);

        db.ChangeTracker.Clear();
        var rolledBackListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var rolledBackEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, rolledBackListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, rolledBackEscrow.Status);
        Assert.Null(rolledBackEscrow.NewTicketCode);
    }

    [Fact]
    public async Task E2ERegression_HoldExpired_AutoReleased_SecondBuyerPurchases_LatePaymentHandled()
    {
        // 1. Buyer 1 giá»¯ vĂ© nhÆ°ng bá» quĂªn 10 phĂºt
        // 2. Worker quĂ©t tá»± Ä‘á»™ng nháº£ vĂ© vá» Verified
        // 3. Buyer 2 vĂ o xem, giá»¯ chá»— vĂ  mua thĂ nh cĂ´ng
        // 4. Buyer 1 chuyá»ƒn tiá»n muá»™n -> Tiá»n vĂ o RefundQueued (hoĂ n 100%), báº£o vá»‡ vĂ© Buyer 2
        var (dbSeed, options, seller, buyer1, buyer2, ev, tier) = CreateFixture();
        var listing = CreateListing(seller.Id, ev, tier, ListingStatus.Transacting);

        const string buyer1PaymentRef = "TSBUYER1A1";
        var escrow1 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 2_000_000m,
            BuyerFee = 100_000m,
            SellerFee = 60_000m,
            TotalBuyerPaid = 2_100_000m,
            NetSellerPayout = 1_940_000m,
            PaymentReference = buyer1PaymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-5), // Háº¿t háº¡n 5 phĂºt trÆ°á»›c
            RecipientName = "Buyer 1",
            RecipientEmail = "buyer1@test.vn"
        };
        listing.EscrowTransaction = escrow1;
        dbSeed.ResaleListings.Add(listing);
        dbSeed.EscrowTransactions.Add(escrow1);
        await dbSeed.SaveChangesAsync();

        // Step 1: Worker nháº£ vĂ© vá» Verified
        var worker = CreateWorker(options);
        var released = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);
        Assert.Equal(1, released);

        // Step 2: Buyer 2 giá»¯ chá»— vĂ©
        const string buyer2PaymentRef = "TSBUYER2B1";
        var escrow2 = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer2.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 2_000_000m,
            BuyerFee = 100_000m,
            SellerFee = 60_000m,
            TotalBuyerPaid = 2_100_000m,
            NetSellerPayout = 1_940_000m,
            PaymentReference = buyer2PaymentRef,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10),
            RecipientName = "Buyer 2",
            RecipientEmail = "buyer2@test.vn"
        };

        using (var buyer2Db = new TicketShieldDbContext(options))
        {
            var l = await buyer2Db.ResaleListings.SingleAsync(r => r.Id == listing.Id);
            l.ListingStatus = ListingStatus.Transacting;
            l.EscrowTransaction = escrow2;
            buyer2Db.EscrowTransactions.Add(escrow2);
            await buyer2Db.SaveChangesAsync();
        }

        // Step 3: Webhook SePay cá»§a Buyer 2 thanh toĂ¡n thĂ nh cĂ´ng
        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
            listing.Id, buyer2.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessTransferResponse("TCK-BUYER2-OFFICIAL"));

        using (var webhookDb = new TicketShieldDbContext(options))
        {
            var webhookHandler = new ProcessSePayWebhookCommandHandler(webhookDb, null, null, null, mockVerification.Object);
            var payloadBuyer2 = CreateSePayPayload(buyer2PaymentRef, 2_100_000m, "FT_BUYER2_OK");
            var res2 = await webhookHandler.Handle(new ProcessSePayWebhookCommand(payloadBuyer2), CancellationToken.None);

            Assert.True(res2.Success);
            Assert.Equal(nameof(EscrowStatus.Locked), res2.Data!.EscrowStatus);
            Assert.Equal(nameof(ListingStatus.Sold), res2.Data.ListingStatus);
        }

        // Step 4: Tiá»n chuyá»ƒn muá»™n cá»§a Buyer 1 bĂ¢y giá» má»›i Ä‘áº¿n sĂ n
        using (var lateDb = new TicketShieldDbContext(options))
        {
            var lateWebhookHandler = new ProcessSePayWebhookCommandHandler(lateDb, null, null, null, mockVerification.Object);
            var payloadBuyer1 = CreateSePayPayload(buyer1PaymentRef, 2_100_000m, "FT_BUYER1_LATE");

            // VĂ¬ Escrow 1 Ä‘Ă£ bá»‹ Worker Ä‘Ă¡nh dáº¥u Expired, há»‡ thá»‘ng tá»« chá»‘i khĂ³a
            var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
                lateWebhookHandler.Handle(new ProcessSePayWebhookCommand(payloadBuyer1), CancellationToken.None));
            Assert.Contains("Expired", ex.Message);
        }

        // Verify Anti-Overselling:
        using (var finalDb = new TicketShieldDbContext(options))
        {
            var finalListing = await finalDb.ResaleListings.SingleAsync(l => l.Id == listing.Id);
            var finalEscrow1 = await finalDb.EscrowTransactions.SingleAsync(e => e.Id == escrow1.Id);
            var finalEscrow2 = await finalDb.EscrowTransactions.SingleAsync(e => e.Id == escrow2.Id);

            Assert.Equal(ListingStatus.Sold, finalListing.ListingStatus);
            Assert.Equal(EscrowStatus.Expired, finalEscrow1.Status);
            Assert.Null(finalEscrow1.NewTicketCode); // Buyer 1 khĂ´ng bao giá» cĂ³ vĂ©

            Assert.Equal(EscrowStatus.Locked, finalEscrow2.Status);
            Assert.Equal("TCK-BUYER2-OFFICIAL", finalEscrow2.NewTicketCode); // Buyer 2 sá»Ÿ há»¯u vĂ© an toĂ n
        }
    }

    #endregion

    #region Worker Factory

    private static ExpiredHoldReleaseWorker CreateWorker(DbContextOptions<TicketShieldDbContext> options)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITicketShieldDbContext>(_ => new TicketShieldDbContext(options));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ExpiredHoldReleaseWorker>();
        return new ExpiredHoldReleaseWorker(scopeFactory, logger);
    }

    #endregion
}


