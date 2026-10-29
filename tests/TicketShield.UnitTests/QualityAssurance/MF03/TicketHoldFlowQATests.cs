using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;
using TicketShield.Application.Features.ResaleListings.Commands.ReleaseListingHold;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// US-3.6_MF03: QA Suite - Luồng giữ chỗ (Reservation & Hold Flow Testing)
/// Kiểm thử tự động toàn diện: giữ chỗ đơn lẻ, combo/bundle, chống trùng lặp,
/// kiểm tra trạng thái khả dụng, tự mua, deadline sự kiện và giải phóng chỗ giữ.
/// </summary>
public class TicketHoldFlowQATests
{
    private static (TicketShieldDbContext db, ShadowUser seller, ShadowUser buyer1, ShadowUser buyer2, Event ev, TicketTier tier) CreateFixture()
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
            Name = "QA Music Festival 2026",
            Venue = "My Dinh National Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(7),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(4),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(6),
            Organizer = organizer
        };

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierName = "VIP Zone",
            OriginalPrice = 1_000_000m,
            Event = ev
        };

        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller@qa.test",
            FullName = "Nguyen Seller"
        };

        var buyer1 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer1@qa.test",
            FullName = "Tran Buyer One"
        };

        var buyer2 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer2@qa.test",
            FullName = "Le Buyer Two"
        };

        db.Organizers.Add(organizer);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(seller, buyer1, buyer2);
        db.SaveChanges();

        return (db, seller, buyer1, buyer2, ev, tier);
    }

    private class MockCurrentUserService(Guid? userId, string? email = "buyer@qa.test") : ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? Email => email;
        public string? Role => "User";
        public bool IsAuthenticated => userId.HasValue;
    }

    private class MockFeeCalculator : IResaleFeeCalculator
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

    [Fact]
    public async Task Hold_SingleVerifiedListing_SuccessfullyHolds_CreatesPendingEscrow_AndSetsHoldingWindow()
    {
        // Arrange
        var (db, seller, buyer1, _, ev, tier) = CreateFixture();
        var handler = new HoldListingForPurchaseCommandHandler(db, new MockFeeCalculator(), new MockCurrentUserService(buyer1.Id));

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "QA-TICKET-001",
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
            RecipientEmail = "buyer1@qa.test"
        }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        // Verify PaymentReference format (TS + 8 alphanumeric = 10 chars)
        Assert.Matches(@"^TS[A-Z0-9]{8}$", result.Data.PaymentReference);

        // Verify DB State
        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Transacting, updatedListing.ListingStatus);

        var escrow = await db.EscrowTransactions.SingleAsync(e => e.ListingId == listing.Id);
        Assert.NotNull(escrow);
        Assert.Equal(EscrowStatus.Pending, escrow.Status);
        Assert.Equal(buyer1.Id, escrow.BuyerId);
        Assert.Equal(seller.Id, escrow.SellerId);
        Assert.Equal(1_050_000m, escrow.TotalBuyerPaid);
        Assert.Equal(970_000m, escrow.NetSellerPayout);
        Assert.NotNull(escrow.UnlockAt);
        Assert.True(escrow.UnlockAt.Value > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Hold_BundleListing_AtomicallyHoldsAllTicketsInBundle()
    {
        // Arrange
        var (db, seller, buyer1, _, ev, tier) = CreateFixture();
        var handler = new HoldListingForPurchaseCommandHandler(db, new MockFeeCalculator(), new MockCurrentUserService(buyer1.Id));

        var bundleId = Guid.NewGuid();
        var listing1 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "QA-BUNDLE-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = bundleId,
            BundleTotalTickets = 2,
            IsBundleAllOrNothing = true
        };
        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "QA-BUNDLE-002",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = bundleId,
            BundleTotalTickets = 2,
            IsBundleAllOrNothing = true
        };

        db.ResaleListings.AddRange(listing1, listing2);
        await db.SaveChangesAsync();

        // Act - Hold one ticket of the bundle
        var result = await handler.Handle(new HoldListingForPurchaseCommand
        {
            ListingId = listing1.Id,
            RecipientName = "Tran Buyer",
            RecipientEmail = "buyer1@qa.test"
        }, CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        db.ChangeTracker.Clear();
        var updatedListings = await db.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.Equal(2, updatedListings.Count);
        Assert.All(updatedListings, l => Assert.Equal(ListingStatus.Transacting, l.ListingStatus));

        var escrow = await db.EscrowTransactions.SingleAsync(e => e.BundleId == bundleId);
        Assert.Equal(EscrowStatus.Pending, escrow.Status);
        Assert.Equal(2_000_000m, escrow.OriginalTicketPrice);
        Assert.Equal(2_100_000m, escrow.TotalBuyerPaid);
    }

    [Fact]
    public async Task Hold_WhenListingAlreadyTransacting_ThrowsBusinessRuleViolation()
    {
        // Arrange
        var (db, seller, buyer1, buyer2, ev, tier) = CreateFixture();
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "QA-HELD-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var activeEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id, // currently held by Buyer 1
            SellerId = seller.Id,
            TotalBuyerPaid = 1_050_000m,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(8), // Active hold window
            PaymentReference = "TSACTIVE01"
        };
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(activeEscrow);
        await db.SaveChangesAsync();

        // Buyer 2 attempts to hold
        var handler = new HoldListingForPurchaseCommandHandler(db, new MockFeeCalculator(), new MockCurrentUserService(buyer2.Id));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new HoldListingForPurchaseCommand { ListingId = listing.Id }, CancellationToken.None));
        Assert.Contains("giữ chỗ bởi người mua khác", ex.Message);
    }

    [Theory]
    [InlineData(ListingStatus.Sold)]
    [InlineData(ListingStatus.Cancelled)]
    [InlineData(ListingStatus.Expired)]
    public async Task Hold_WhenListingNotInVerifiedStatus_ThrowsBusinessRuleViolation(ListingStatus status)
    {
        // Arrange
        var (db, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "QA-UNAVAIL-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = status
        };
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(db, new MockFeeCalculator(), new MockCurrentUserService(buyer1.Id));

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new HoldListingForPurchaseCommand { ListingId = listing.Id }, CancellationToken.None));
    }

    [Fact]
    public async Task Hold_WhenSellerAttemptsToHoldOwnListing_ThrowsBadRequestException()
    {
        // Arrange
        var (db, seller, _, _, ev, tier) = CreateFixture();
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "QA-SELF-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified
        };
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync();

        // Seller tries to hold their own ticket
        var handler = new HoldListingForPurchaseCommandHandler(db, new MockFeeCalculator(), new MockCurrentUserService(seller.Id));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new HoldListingForPurchaseCommand { ListingId = listing.Id }, CancellationToken.None));
        Assert.Contains("không thể tự mua vé của chính mình", ex.Message);
    }

    [Fact]
    public async Task Hold_WhenEventPastResaleCutoff_ThrowsBusinessRuleViolation()
    {
        // Arrange
        var (db, seller, buyer1, _, ev, tier) = CreateFixture();
        ev.EventStartAt = DateTimeOffset.UtcNow.AddHours(1); // within 2 hour cutoff
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "QA-CUTOFF-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified
        };
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(db, new MockFeeCalculator(), new MockCurrentUserService(buyer1.Id));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new HoldListingForPurchaseCommand { ListingId = listing.Id }, CancellationToken.None));
        Assert.Contains("thời hạn", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReleaseHold_WhenBuyerCancels_TransitionsListingBackToVerified_AndCancelsEscrow()
    {
        // Arrange
        var (db, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "QA-RELEASE-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            TotalBuyerPaid = 1_050_000m,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10),
            PaymentReference = "TSRELEASE1"
        };

        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        var handler = new ReleaseListingHoldCommandHandler(db, new MockCurrentUserService(buyer1.Id));

        // Act
        var response = await handler.Handle(new ReleaseListingHoldCommand(listing.Id), CancellationToken.None);

        // Assert
        Assert.True(response.Success);

        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, updatedListing.ListingStatus);
        Assert.Equal(EscrowStatus.Cancelled, updatedEscrow.Status);
    }

    [Fact]
    public async Task ReleaseHold_WhenNonHolderBuyerAttemptsRelease_ThrowsForbiddenAccess()
    {
        // Arrange
        var (db, seller, buyer1, buyer2, ev, tier) = CreateFixture();
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "QA-FORBID-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id, // Held by Buyer 1
            SellerId = seller.Id,
            TotalBuyerPaid = 1_050_000m,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10),
            PaymentReference = "TSFORBID01"
        };

        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        // Buyer 2 attempts to release Buyer 1's hold
        var handler = new ReleaseListingHoldCommandHandler(db, new MockCurrentUserService(buyer2.Id));

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new ReleaseListingHoldCommand(listing.Id), CancellationToken.None));
    }

    [Fact]
    public async Task ReleaseHold_WhenListingAlreadySold_ThrowsBusinessRuleViolation()
    {
        // Arrange - Listing is already Sold
        var (db, seller, buyer1, _, ev, tier) = CreateFixture();
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "QA-LOCKED-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Sold // already paid & sold
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            TotalBuyerPaid = 1_050_000m,
            Status = EscrowStatus.Locked,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10),
            PaymentReference = "TSLOCKED01"
        };

        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        var handler = new ReleaseListingHoldCommandHandler(db, new MockCurrentUserService(buyer1.Id));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new ReleaseListingHoldCommand(listing.Id), CancellationToken.None));
        Assert.Contains("không ở trạng thái giữ chỗ", ex.Message);
    }
}
