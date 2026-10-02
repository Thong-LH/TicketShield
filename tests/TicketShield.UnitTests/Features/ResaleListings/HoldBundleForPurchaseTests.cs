using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;
using TicketShield.Application.Features.ResaleListings.Commands.ReleaseListingHold;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Workers;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class HoldBundleForPurchaseTests
{
    private static (TicketShieldDbContext dbContext, ShadowUser seller, ShadowUser buyer1, ShadowUser buyer2, ServiceProvider serviceProvider) CreateInMemoryDbContextWithServices()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();

        services.AddDbContext<TicketShieldDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));

        services.AddScoped<ITicketShieldDbContext>(provider =>
            provider.GetRequiredService<TicketShieldDbContext>());

        var serviceProvider = services.BuildServiceProvider();
        var dbContext = serviceProvider.GetRequiredService<TicketShieldDbContext>();

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "V-Concert Organizer",
            OfficialEmail = "vconcert@test.com"
        };
        var testEvent = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "V-Concert Hanoi 2026",
            Venue = "Trung tam Hoi nghi Quoc gia",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(15),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(15).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(14),
            Organizer = organizer
        };
        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierName = "VIP",
            OriginalPrice = 2_000_000m,
            Event = testEvent
        };
        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller@ticketshield.vn",
            FullName = "Nguyen Van Seller"
        };
        var buyer1 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer1@ticketshield.vn",
            FullName = "Tran Van Buyer One"
        };
        var buyer2 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer2@ticketshield.vn",
            FullName = "Le Thi Buyer Two"
        };

        dbContext.Organizers.Add(organizer);
        dbContext.Events.Add(testEvent);
        dbContext.TicketTiers.Add(tier);
        dbContext.ShadowUsers.AddRange(seller, buyer1, buyer2);
        dbContext.SaveChanges();

        return (dbContext, seller, buyer1, buyer2, serviceProvider);
    }

    private class MockCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; }
        public string? Email => "buyer@ticketshield.vn";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;

        public MockCurrentUserService(Guid? userId) => UserId = userId;
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

    private static List<ResaleListing> CreateBundleListings(Guid bundleId, Guid eventId, Guid tierId, Guid sellerId, int count = 3)
    {
        var listings = new List<ResaleListing>();
        for (int i = 1; i <= count; i++)
        {
            listings.Add(new ResaleListing
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                TierId = tierId,
                SellerId = sellerId,
                OriginalTicketCode = $"TCK-BUNDLE-{i:000}",
                OriginalPrice = 1_000_000m,
                ResalePrice = 1_000_000m,
                ListingStatus = ListingStatus.Verified,
                BundleId = bundleId,
                IsBundleAllOrNothing = true,
                BundleTotalTickets = count,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        return listings;
    }

    [Fact]
    public async Task HoldBundle_AllOrNothing_ShouldLockAllListingsAtomically()
    {
        // Arrange
        var (dbContext, seller, buyer1, _, _) = CreateInMemoryDbContextWithServices();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer1.Id);

        var eventId = (await dbContext.Events.FirstAsync()).Id;
        var tierId = (await dbContext.TicketTiers.FirstAsync()).Id;
        var bundleId = Guid.NewGuid();
        var listings = CreateBundleListings(bundleId, eventId, tierId, seller.Id, 3);
        dbContext.ResaleListings.AddRange(listings);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listings[0].Id); // Hold anchor listing

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Transacting", result.Data.ListingStatus);
        Assert.Equal(bundleId, result.Data.BundleId);
        Assert.Equal(3, result.Data.BundleTotalTickets);
        Assert.NotNull(result.Data.BundleItems);
        Assert.Equal(3, result.Data.BundleItems.Count);

        // All 3 listings in DB should be Transacting
        var updatedListings = await dbContext.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.All(updatedListings, l => Assert.Equal(ListingStatus.Transacting, l.ListingStatus));
    }

    [Fact]
    public async Task HoldBundle_ShouldCreateSingleEscrowWithAggregatedFees()
    {
        // Arrange
        var (dbContext, seller, buyer1, _, _) = CreateInMemoryDbContextWithServices();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer1.Id);

        var eventId = (await dbContext.Events.FirstAsync()).Id;
        var tierId = (await dbContext.TicketTiers.FirstAsync()).Id;
        var bundleId = Guid.NewGuid();
        var listings = CreateBundleListings(bundleId, eventId, tierId, seller.Id, 3);
        dbContext.ResaleListings.AddRange(listings);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listings[0].Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert: Exactly 1 escrow created for the entire bundle
        var bundleEscrows = await dbContext.EscrowTransactions.Where(e => e.BundleId == bundleId).ToListAsync();
        Assert.Single(bundleEscrows);

        var escrow = bundleEscrows[0];
        Assert.Equal(listings[0].Id, escrow.ListingId); // Anchor listing
        Assert.Equal(buyer1.Id, escrow.BuyerId);
        Assert.Equal(seller.Id, escrow.SellerId);
        Assert.Equal(EscrowStatus.Pending, escrow.Status);

        // Aggregated: 3 * 1,000,000 = 3,000,000 base, 3 * 50,000 = 150,000 buyer fee => 3,150,000 total
        Assert.Equal(3_000_000m, escrow.OriginalTicketPrice);
        Assert.Equal(150_000m, escrow.BuyerFee);
        Assert.Equal(90_000m, escrow.SellerFee);
        Assert.Equal(3_150_000m, escrow.TotalBuyerPaid);
        Assert.Equal(2_910_000m, escrow.NetSellerPayout);

        Assert.Equal(3_150_000m, result.Data.TotalBuyerPaid);
    }

    [Fact]
    public async Task HoldBundle_EscrowShouldHaveBundleIdSet()
    {
        // Arrange
        var (dbContext, seller, buyer1, _, _) = CreateInMemoryDbContextWithServices();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer1.Id);

        var eventId = (await dbContext.Events.FirstAsync()).Id;
        var tierId = (await dbContext.TicketTiers.FirstAsync()).Id;
        var bundleId = Guid.NewGuid();
        var listings = CreateBundleListings(bundleId, eventId, tierId, seller.Id, 2);
        dbContext.ResaleListings.AddRange(listings);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listings[0].Id);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        var escrow = await dbContext.EscrowTransactions.FirstAsync(e => e.ListingId == listings[0].Id);
        Assert.NotNull(escrow.BundleId);
        Assert.Equal(bundleId, escrow.BundleId.Value);
    }

    [Fact]
    public async Task HoldBundle_WhenOneListingTransactingByOther_ShouldThrow()
    {
        // Arrange
        var (dbContext, seller, buyer1, buyer2, _) = CreateInMemoryDbContextWithServices();
        var feeCalculator = new MockResaleFeeCalculator();

        var eventId = (await dbContext.Events.FirstAsync()).Id;
        var tierId = (await dbContext.TicketTiers.FirstAsync()).Id;
        var bundleId = Guid.NewGuid();
        var listings = CreateBundleListings(bundleId, eventId, tierId, seller.Id, 2);

        // Listing 1 is held by buyer2
        listings[1].ListingStatus = ListingStatus.Transacting;
        var otherEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listings[1].Id,
            BundleId = bundleId,
            BuyerId = buyer2.Id,
            SellerId = seller.Id,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(5),
            PaymentReference = "TSOTHER001"
        };
        listings[1].EscrowTransactions = new List<EscrowTransaction> { otherEscrow };

        dbContext.ResaleListings.AddRange(listings);
        dbContext.EscrowTransactions.Add(otherEscrow);
        await dbContext.SaveChangesAsync();

        // buyer1 tries to hold listing 0
        var currentUserService = new MockCurrentUserService(buyer1.Id);
        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listings[0].Id);

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(command, CancellationToken.None));

        // Listing 0 should remain Verified
        var listing0 = await dbContext.ResaleListings.FindAsync(listings[0].Id);
        Assert.Equal(ListingStatus.Verified, listing0!.ListingStatus);
    }

    [Fact]
    public async Task HoldBundle_RenewBySameBuyer_ShouldExtendUnlockAt()
    {
        // Arrange
        var (dbContext, seller, buyer1, _, _) = CreateInMemoryDbContextWithServices();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer1.Id);

        var eventId = (await dbContext.Events.FirstAsync()).Id;
        var tierId = (await dbContext.TicketTiers.FirstAsync()).Id;
        var bundleId = Guid.NewGuid();
        var listings = CreateBundleListings(bundleId, eventId, tierId, seller.Id, 2);
        dbContext.ResaleListings.AddRange(listings);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);

        // 1st hold
        var firstResult = await handler.Handle(new HoldListingForPurchaseCommand(listings[0].Id), CancellationToken.None);
        var originalUnlockAt = firstResult.Data.UnlockAt;
        var originalPaymentRef = firstResult.Data.PaymentReference;

        // 2nd hold by same buyer (renew)
        var secondResult = await handler.Handle(new HoldListingForPurchaseCommand(listings[0].Id), CancellationToken.None);

        // Assert: Still only 1 escrow, UnlockAt extended, same payment ref
        var bundleEscrows = await dbContext.EscrowTransactions.Where(e => e.BundleId == bundleId).ToListAsync();
        Assert.Single(bundleEscrows);
        Assert.Equal(originalPaymentRef, secondResult.Data.PaymentReference);
        Assert.True(secondResult.Data.UnlockAt >= originalUnlockAt);
    }

    [Fact]
    public async Task HoldBundle_ShouldGenerateSinglePaymentReference()
    {
        // Arrange
        var (dbContext, seller, buyer1, _, _) = CreateInMemoryDbContextWithServices();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer1.Id);

        var eventId = (await dbContext.Events.FirstAsync()).Id;
        var tierId = (await dbContext.TicketTiers.FirstAsync()).Id;
        var bundleId = Guid.NewGuid();
        var listings = CreateBundleListings(bundleId, eventId, tierId, seller.Id, 3);
        dbContext.ResaleListings.AddRange(listings);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listings[0].Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.StartsWith("TS", result.Data.PaymentReference);
        Assert.Equal(10, result.Data.PaymentReference.Length);

        var escrow = await dbContext.EscrowTransactions.FirstAsync(e => e.BundleId == bundleId);
        Assert.Equal(result.Data.PaymentReference, escrow.PaymentReference);
    }

    [Fact]
    public async Task ReleaseBundle_ShouldReleaseAllListings()
    {
        // Arrange
        var (dbContext, seller, buyer1, _, _) = CreateInMemoryDbContextWithServices();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer1.Id);

        var eventId = (await dbContext.Events.FirstAsync()).Id;
        var tierId = (await dbContext.TicketTiers.FirstAsync()).Id;
        var bundleId = Guid.NewGuid();
        var listings = CreateBundleListings(bundleId, eventId, tierId, seller.Id, 3);
        dbContext.ResaleListings.AddRange(listings);
        await dbContext.SaveChangesAsync();

        // Hold the bundle first
        var holdHandler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        await holdHandler.Handle(new HoldListingForPurchaseCommand(listings[0].Id), CancellationToken.None);

        // Verify all are Transacting
        var heldListings = await dbContext.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.All(heldListings, l => Assert.Equal(ListingStatus.Transacting, l.ListingStatus));

        // Act: Release via anchor listing
        var releaseHandler = new ReleaseListingHoldCommandHandler(dbContext, currentUserService);
        var releaseCommand = new ReleaseListingHoldCommand(listings[0].Id);
        var releaseResult = await releaseHandler.Handle(releaseCommand, CancellationToken.None);

        // Assert
        Assert.NotNull(releaseResult);
        Assert.True(releaseResult.Success);

        // All 3 listings should revert to Verified
        var releasedListings = await dbContext.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.All(releasedListings, l => Assert.Equal(ListingStatus.Verified, l.ListingStatus));

        // Bundle escrow status should be Cancelled
        var escrow = await dbContext.EscrowTransactions.FirstAsync(e => e.BundleId == bundleId);
        Assert.Equal(EscrowStatus.Cancelled, escrow.Status);
    }

    [Fact]
    public async Task ExpireBundle_WhenEscrowExpires_ShouldReleaseAllListings()
    {
        // Arrange
        var (dbContext, seller, buyer1, _, serviceProvider) = CreateInMemoryDbContextWithServices();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var eventId = (await dbContext.Events.FirstAsync()).Id;
        var tierId = (await dbContext.TicketTiers.FirstAsync()).Id;
        var bundleId = Guid.NewGuid();
        var listings = CreateBundleListings(bundleId, eventId, tierId, seller.Id, 3);
        foreach (var l in listings)
        {
            l.ListingStatus = ListingStatus.Transacting;
        }

        var expiredEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listings[0].Id,
            BundleId = bundleId,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            PaymentReference = "TSEXP00001",
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-2), // Expired 2 minutes ago
            OriginalTicketPrice = 3_000_000m,
            BuyerFee = 150_000m,
            SellerFee = 90_000m,
            TotalBuyerPaid = 3_150_000m,
            NetSellerPayout = 2_910_000m
        };
        listings[0].EscrowTransactions = new List<EscrowTransaction> { expiredEscrow };

        dbContext.ResaleListings.AddRange(listings);
        dbContext.EscrowTransactions.Add(expiredEscrow);
        await dbContext.SaveChangesAsync();

        var worker = new ExpiredHoldReleaseWorker(scopeFactory, NullLogger<ExpiredHoldReleaseWorker>.Instance);

        // Act
        var releasedCount = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);

        // Assert: 3 listings reverted
        Assert.Equal(3, releasedCount);

        dbContext.ChangeTracker.Clear();
        var refreshedListings = await dbContext.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.All(refreshedListings, l => Assert.Equal(ListingStatus.Verified, l.ListingStatus));

        var refreshedEscrow = await dbContext.EscrowTransactions.FindAsync(expiredEscrow.Id);
        Assert.Equal(EscrowStatus.Expired, refreshedEscrow!.Status);
    }

    [Fact]
    public async Task HoldSingleListing_ShouldNotAffectBundleLogic()
    {
        // Arrange: Single listing with BundleId = null
        var (dbContext, seller, buyer1, _, _) = CreateInMemoryDbContextWithServices();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer1.Id);

        var eventId = (await dbContext.Events.FirstAsync()).Id;
        var tierId = (await dbContext.TicketTiers.FirstAsync()).Id;

        var singleListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            TierId = tierId,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-SINGLE-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = null,
            IsBundleAllOrNothing = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ResaleListings.Add(singleListing);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(singleListing.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Null(result.Data.BundleId);
        Assert.Null(result.Data.BundleTotalTickets);
        Assert.Null(result.Data.BundleItems);

        var escrow = await dbContext.EscrowTransactions.FirstAsync(e => e.ListingId == singleListing.Id);
        Assert.Null(escrow.BundleId);
        Assert.Equal(1_050_000m, escrow.TotalBuyerPaid);
    }
}
