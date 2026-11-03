using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TicketShield.Application.Common.Configurations;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Application.Features.ResaleListings.Commands.CancelResaleListing;
using TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using TicketShield.Infrastructure.Workers;
using Xunit;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// TEST-3.6.1_Unit tests for Hold timeout, VietQR generation and auto-release
/// Description: Test logic đếm ngược 10 phút, kiểm tra vé khóa TRANSACTING và tự động mở lại VERIFIED khi hết hạn.
/// </summary>
public class HoldTimeoutVietQrAutoReleaseTests
{
    private static readonly VietQrSettings TestVietQrSettings = new()
    {
        Provider = "SePAY",
        BaseUrl = "https://my.sepay.vn/userapi",
        ApiKey = "sepay_test_api_key_2026",
        WebhookSecret = "sepay_test_webhook_secret_2026",
        BankBin = "970422", // MBBank
        AccountNumber = "0938434102",
        AccountName = "CONG TY TICKETSHIELD",
        QrTemplate = "compact2"
    };

    private static (TicketShieldDbContext db, ServiceProvider provider, ShadowUser seller, ShadowUser buyer1, ShadowUser buyer2, Event ev, TicketTier tier) CreateFixture()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();

        services.AddDbContext<TicketShieldDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));

        services.AddScoped<ITicketShieldDbContext>(p => p.GetRequiredService<TicketShieldDbContext>());

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<TicketShieldDbContext>();

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "V-Concert Organizer",
            OfficialEmail = "organizer@vconcert.test"
        };

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "Live Arena 2026",
            Venue = "My Dinh National Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(10),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(4),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(9),
            Organizer = organizer
        };

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierName = "VIP Diamond",
            OriginalPrice = 1_500_000m,
            Event = ev
        };

        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller@test.vn",
            FullName = "Nguyen Seller"
        };

        var buyer1 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer1@test.vn",
            FullName = "Tran Buyer One"
        };

        var buyer2 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer2@test.vn",
            FullName = "Le Buyer Two"
        };

        db.Organizers.Add(organizer);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(seller, buyer1, buyer2);
        db.SaveChanges();

        return (db, provider, seller, buyer1, buyer2, ev, tier);
    }

    private class MockCurrentUserService(Guid? userId, string? email = "buyer1@test.vn") : ICurrentUserService
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

    #region 1. Test logic đếm ngược 10 phút (10-minute hold countdown & timeout)

    [Fact]
    public async Task Hold_InitializesExact10MinuteCountdown_UnlockAtIsNowPlus10Minutes()
    {
        // Arrange
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var feeCalculator = new MockResaleFeeCalculator();
        var vietQrService = new VietQrService(Options.Create(TestVietQrSettings));
        var handler = new HoldListingForPurchaseCommandHandler(db, feeCalculator, new MockCurrentUserService(buyer1.Id), vietQrService);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-COUNTDOWN-001",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Verified
        };
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync();

        var beforeHold = DateTimeOffset.UtcNow;

        // Act
        var result = await handler.Handle(new HoldListingForPurchaseCommand
        {
            ListingId = listing.Id,
            RecipientName = "Tran Buyer",
            RecipientEmail = "buyer1@test.vn"
        }, CancellationToken.None);

        var afterHold = DateTimeOffset.UtcNow;

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);

        db.ChangeTracker.Clear();
        var escrow = await db.EscrowTransactions.SingleAsync(e => e.ListingId == listing.Id);

        Assert.Equal(EscrowStatus.Pending, escrow.Status);
        Assert.NotNull(escrow.UnlockAt);

        // Verify countdown is exactly 10 minutes from now (allowing +- 5 seconds delta)
        var expectedMinUnlock = beforeHold.AddMinutes(10);
        var expectedMaxUnlock = afterHold.AddMinutes(10);
        Assert.InRange(escrow.UnlockAt.Value, expectedMinUnlock, expectedMaxUnlock);

        // Verify remaining countdown time is approximately 10 minutes
        var remainingSeconds = (escrow.UnlockAt.Value - DateTimeOffset.UtcNow).TotalSeconds;
        Assert.InRange(remainingSeconds, 590, 600);
    }

    [Fact]
    public async Task Hold_Within10MinuteWindow_BlocksOtherBuyersFromHolding()
    {
        // Arrange - Ticket held with 8 minutes remaining in 10-minute window
        var (db, _, seller, buyer1, buyer2, ev, tier) = CreateFixture();
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-LOCK-001",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var activeEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            TotalBuyerPaid = 1_575_000m,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(8), // 8 mins left of 10 min window
            PaymentReference = "TSACTIVE001"
        };
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(activeEscrow);
        await db.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(db, new MockResaleFeeCalculator(), new MockCurrentUserService(buyer2.Id));

        // Act & Assert - Buyer 2 cannot steal hold during active 10-minute window
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new HoldListingForPurchaseCommand { ListingId = listing.Id }, CancellationToken.None));
        Assert.Contains("giữ chỗ bởi người mua khác", ex.Message);
    }

    [Fact]
    public async Task Hold_Within10MinuteWindow_SameBuyerCanRenewWithoutChangingPaymentReference()
    {
        // Arrange - Buyer 1 holding ticket with existing payment reference
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-RENEW-001",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var activeEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            TotalBuyerPaid = 1_575_000m,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(5),
            PaymentReference = "TSRENEW001"
        };
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(activeEscrow);
        await db.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(db, new MockResaleFeeCalculator(), new MockCurrentUserService(buyer1.Id));

        // Act - Buyer 1 re-submits hold
        var result = await handler.Handle(new HoldListingForPurchaseCommand
        {
            ListingId = listing.Id,
            RecipientName = "Tran Buyer Updated"
        }, CancellationToken.None);

        // Assert - Preserves original payment reference and extends 10 minute window
        Assert.True(result.Success);
        Assert.Equal("TSRENEW001", result.Data!.PaymentReference);

        db.ChangeTracker.Clear();
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == activeEscrow.Id);
        Assert.Equal("Tran Buyer Updated", updatedEscrow.RecipientName);
        Assert.True(updatedEscrow.UnlockAt > DateTimeOffset.UtcNow.AddMinutes(9));
    }

    #endregion

    #region 2. Kiểm tra vé khóa TRANSACTING

    [Fact]
    public async Task Hold_SingleListing_LocksTicketInTransactingStatus()
    {
        // Arrange
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var handler = new HoldListingForPurchaseCommandHandler(db, new MockResaleFeeCalculator(), new MockCurrentUserService(buyer1.Id));

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-LOCK-STATUS",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Verified
        };
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync();

        // Act
        var result = await handler.Handle(new HoldListingForPurchaseCommand { ListingId = listing.Id }, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Transacting", result.Data!.ListingStatus);

        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Transacting, updatedListing.ListingStatus);
    }

    [Fact]
    public async Task Hold_BundleListing_AtomicallyLocksAllTicketsInBundleToTransacting()
    {
        // Arrange - Bundle with 3 tickets
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var bundleId = Guid.NewGuid();
        var listing1 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "COMBO-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = bundleId,
            BundleTotalTickets = 3,
            IsBundleAllOrNothing = true
        };
        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "COMBO-002",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = bundleId,
            BundleTotalTickets = 3,
            IsBundleAllOrNothing = true
        };
        var listing3 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "COMBO-003",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = bundleId,
            BundleTotalTickets = 3,
            IsBundleAllOrNothing = true
        };

        db.ResaleListings.AddRange(listing1, listing2, listing3);
        await db.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(db, new MockResaleFeeCalculator(), new MockCurrentUserService(buyer1.Id));

        // Act - Hold anchor listing of the bundle
        var result = await handler.Handle(new HoldListingForPurchaseCommand { ListingId = listing1.Id }, CancellationToken.None);

        // Assert - All 3 tickets in combo atomically lock to Transacting
        Assert.True(result.Success);

        db.ChangeTracker.Clear();
        var allComboListings = await db.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.Equal(3, allComboListings.Count);
        Assert.All(allComboListings, l => Assert.Equal(ListingStatus.Transacting, l.ListingStatus));
    }

    [Fact]
    public async Task CancelListing_WhenTicketIsLockedInTransacting_ThrowsBusinessRuleViolation()
    {
        // Arrange - Listing is currently locked in Transacting
        var (db, _, seller, _, _, ev, tier) = CreateFixture();
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-CANT-CANCEL",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting
        };
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync();

        var cancelHandler = new CancelResaleListingCommandHandler(db, new MockCurrentUserService(seller.Id));

        // Act & Assert - Seller cannot cancel their ticket while a buyer is transacting
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            cancelHandler.Handle(new CancelResaleListingCommand(listing.Id), CancellationToken.None));
        Assert.Contains("Transacting", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 3. Sinh mã thanh toán VietQR (VietQR Generation & NAPAS 247 Metadata)

    [Fact]
    public async Task Hold_GeneratesValidVietQr_WithPlatformAccount_Amount_AndPaymentReference()
    {
        // Arrange
        var (db, _, seller, buyer1, _, ev, tier) = CreateFixture();
        var feeCalculator = new MockResaleFeeCalculator();
        var vietQrService = new VietQrService(Options.Create(TestVietQrSettings));
        var handler = new HoldListingForPurchaseCommandHandler(db, feeCalculator, new MockCurrentUserService(buyer1.Id), vietQrService);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-VIETQR-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified
        };
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync();

        // Act
        var result = await handler.Handle(new HoldListingForPurchaseCommand
        {
            ListingId = listing.Id,
            RecipientName = "Tran Buyer",
            RecipientEmail = "buyer1@test.vn"
        }, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.Data!;

        // 1. PaymentReference format: TS + 8 alphanumeric = 10 chars
        Assert.Matches(@"^TS[A-Z0-9]{8}$", data.PaymentReference);

        // 2. Platform Bank Account details
        Assert.Equal("970422", data.BankBin);
        Assert.Equal("0938434102", data.AccountNumber);
        Assert.Equal("CONG TY TICKETSHIELD", data.AccountName);

        // 3. VietQR QuickLink and Image URL
        Assert.False(string.IsNullOrWhiteSpace(data.QrImageUrl));
        Assert.StartsWith("https://img.vietqr.io/image/970422-0938434102-compact2.png", data.QrImageUrl);
        Assert.Contains($"amount=1050000", data.QrImageUrl); // 1,000,000 + 50,000 fee
        Assert.Contains($"addInfo={data.PaymentReference}", data.QrImageUrl);
        Assert.Contains("accountName=CONG+TY+TICKETSHIELD", data.QrImageUrl);

        // 4. QuickLink URL
        Assert.Contains(data.PaymentReference, data.QuickLinkUrl);
    }

    #endregion

    #region 4. Tự động mở lại VERIFIED khi hết hạn (Auto-Release to VERIFIED Upon Expiration)

    [Fact]
    public async Task AutoRelease_WhenHoldExpiredPast10Minutes_RevertsListingToVerified_AndSetsEscrowExpired()
    {
        // Arrange - Hold expired (UnlockAt was 15 minutes ago)
        var (db, provider, seller, buyer1, _, ev, tier) = CreateFixture();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-EXPIRED-HOLD",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting,
            Event = ev
        };
        var expiredEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            PaymentReference = "TSEXPIRED10",
            TotalBuyerPaid = 1_575_000m,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-5), // Expired 5 mins ago
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15) // Started 15 mins ago
        };
        listing.EscrowTransactions.Add(expiredEscrow);

        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(expiredEscrow);
        await db.SaveChangesAsync();

        var worker = new ExpiredHoldReleaseWorker(scopeFactory, NullLogger<ExpiredHoldReleaseWorker>.Instance);

        // Act - Background worker runs release cycle
        var revertedCount = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, revertedCount);

        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == expiredEscrow.Id);

        // Ticket automatically returns to VERIFIED and ready for other buyers
        Assert.Equal(ListingStatus.Verified, updatedListing.ListingStatus);
        Assert.Equal(EscrowStatus.Expired, updatedEscrow.Status);
    }

    [Fact]
    public async Task AutoRelease_WhenBundleHoldExpired_RevertsAllListingsInBundleToVerified()
    {
        // Arrange - Bundle of 3 tickets expired
        var (db, provider, seller, buyer1, _, ev, tier) = CreateFixture();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var bundleId = Guid.NewGuid();

        var listing1 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "BUNDLE-EXP-1",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            BundleTotalTickets = 3,
            Event = ev
        };
        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "BUNDLE-EXP-2",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            BundleTotalTickets = 3,
            Event = ev
        };
        var listing3 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "BUNDLE-EXP-3",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            BundleTotalTickets = 3,
            Event = ev
        };

        var expiredEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing1.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            BundleId = bundleId,
            PaymentReference = "TSBUNDLEEXP",
            TotalBuyerPaid = 3_150_000m,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-2) // Expired 2 mins ago
        };

        db.ResaleListings.AddRange(listing1, listing2, listing3);
        db.EscrowTransactions.Add(expiredEscrow);
        await db.SaveChangesAsync();

        var worker = new ExpiredHoldReleaseWorker(scopeFactory, NullLogger<ExpiredHoldReleaseWorker>.Instance);

        // Act
        var revertedCount = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);

        // Assert - All 3 tickets in the combo automatically revert to VERIFIED
        Assert.Equal(3, revertedCount);

        db.ChangeTracker.Clear();
        var allListings = await db.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.All(allListings, l => Assert.Equal(ListingStatus.Verified, l.ListingStatus));

        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == expiredEscrow.Id);
        Assert.Equal(EscrowStatus.Expired, updatedEscrow.Status);
    }

    [Fact]
    public async Task AutoRelease_WhenHoldNotYetExpired_LeavesListingInTransacting()
    {
        // Arrange - Active hold with 5 minutes remaining
        var (db, provider, seller, buyer1, _, ev, tier) = CreateFixture();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ACTIVE-STILL",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting,
            Event = ev
        };
        var activeEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            PaymentReference = "TSSTILLACTIVE",
            TotalBuyerPaid = 1_575_000m,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(5) // Still active for 5 mins
        };

        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(activeEscrow);
        await db.SaveChangesAsync();

        var worker = new ExpiredHoldReleaseWorker(scopeFactory, NullLogger<ExpiredHoldReleaseWorker>.Instance);

        // Act
        var revertedCount = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);

        // Assert - No tickets reverted, ticket stays locked in Transacting
        Assert.Equal(0, revertedCount);

        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == activeEscrow.Id);

        Assert.Equal(ListingStatus.Transacting, updatedListing.ListingStatus);
        Assert.Equal(EscrowStatus.Pending, updatedEscrow.Status);
    }

    [Fact]
    public async Task AutoRelease_WhenEventPast2HourCutoff_ExpiresListingPermanently()
    {
        // Arrange - Hold expired AND event starts in 1 hour (< 2h cutoff)
        var (db, provider, seller, buyer1, _, ev, tier) = CreateFixture();
        ev.EventStartAt = DateTimeOffset.UtcNow.AddHours(1); // within 2 hour cutoff
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-CUTOFF-EXP",
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Transacting,
            Event = ev
        };
        var expiredEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            PaymentReference = "TSCUTOFF01",
            TotalBuyerPaid = 1_575_000m,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(expiredEscrow);
        await db.SaveChangesAsync();

        var worker = new ExpiredHoldReleaseWorker(scopeFactory, NullLogger<ExpiredHoldReleaseWorker>.Instance);

        // Act
        var revertedCount = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);

        // Assert - Past cutoff, moves to EXPIRED instead of VERIFIED
        Assert.Equal(1, revertedCount);

        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Expired, updatedListing.ListingStatus);
    }

    #endregion
}
