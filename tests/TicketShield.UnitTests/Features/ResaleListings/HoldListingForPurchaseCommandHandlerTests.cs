using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class HoldListingForPurchaseCommandHandlerTests
{
    private static (TicketShieldDbContext dbContext, ShadowUser seller, ShadowUser buyer) CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new TicketShieldDbContext(options);

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
        var buyer = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer@ticketshield.vn",
            FullName = "Tran Van Buyer"
        };

        context.Organizers.Add(organizer);
        context.Events.Add(testEvent);
        context.TicketTiers.Add(tier);
        context.ShadowUsers.AddRange(seller, buyer);
        context.SaveChanges();

        return (context, seller, buyer);
    }

    private class MockCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; }
        public string? Email { get; }
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;

        public MockCurrentUserService(Guid? userId, string? email = "buyer@ticketshield.vn")
        {
            UserId = userId;
            Email = email;
        }
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

    [Fact]
    public async Task Handle_ValidVerifiedListing_ShouldHoldSuccessfullyAndGeneratePaymentReference()
    {
        // Arrange
        var (dbContext, seller, buyer) = CreateInMemoryDbContext();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer.Id);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-HOLD-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listing.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Transacting", result.Data.ListingStatus);
        Assert.StartsWith("TS", result.Data.PaymentReference);
        Assert.Equal(10, result.Data.PaymentReference.Length); // TS + 8 chars = 10 chars total
        Assert.Equal(1_050_000m, result.Data.TotalBuyerPaid); // 1,000,000 + 5%

        var updatedListing = await dbContext.ResaleListings.Include(l => l.EscrowTransactions).FirstAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Transacting, updatedListing.ListingStatus);
        Assert.NotNull(updatedListing.EscrowTransaction);
        Assert.Equal(buyer.Id, updatedListing.EscrowTransaction.BuyerId);
        Assert.Equal(EscrowStatus.Pending, updatedListing.EscrowTransaction.Status);
    }

    [Fact]
    public async Task Handle_WhenBuyerIsSeller_ShouldThrowBadRequestException()
    {
        // Arrange
        var (dbContext, seller, _) = CreateInMemoryDbContext();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(seller.Id); // Seller trying to buy own ticket

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-HOLD-002",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified
        };
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listing.Id);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("You cannot purchase your own ticket.", ex.Message);
    }

    [Fact]
    public async Task Handle_WhenListingIsPrivateAndTokenInvalid_ShouldThrowForbiddenAccessException()
    {
        // Arrange
        var (dbContext, seller, buyer) = CreateInMemoryDbContext();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer.Id);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-PRIVATE-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            IsPrivate = true,
            PrivateAccessToken = "secret_token_123",
            ListingStatus = ListingStatus.Verified
        };
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listing.Id, privateAccessToken: "wrong_token");

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenListingIsTransactingWithActiveHold_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var (dbContext, seller, buyer) = CreateInMemoryDbContext();
        var otherBuyerId = Guid.NewGuid();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer.Id);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-HOLD-003",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var activeEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = otherBuyerId, // Active hold by another buyer
            SellerId = seller.Id,
            PaymentReference = "TS11111111",
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(5) // Still active (expires in 5 mins)
        };
        listing.EscrowTransaction = activeEscrow;
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listing.Id);

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenListingIsTransactingWithExpiredHold_ShouldAllowNewHoldAndResetEscrow()
    {
        // Arrange
        var (dbContext, seller, buyer) = CreateInMemoryDbContext();
        var previousBuyerId = Guid.NewGuid();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer.Id);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-HOLD-004",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var expiredEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = previousBuyerId,
            SellerId = seller.Id,
            PaymentReference = "TSEXPIRED1",
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-2) // Expired 2 minutes ago
        };
        listing.EscrowTransaction = expiredEscrow;
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listing.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(previousBuyerId, expiredEscrow.BuyerId); // Original expired escrow buyer preserved!
        Assert.NotEqual("TSEXPIRED1", result.Data.PaymentReference); // New unique payment reference

        var updatedListing = await dbContext.ResaleListings.Include(l => l.EscrowTransactions).FirstAsync(l => l.Id == listing.Id);
        Assert.Equal(2, updatedListing.EscrowTransactions.Count);
        var newEscrow = updatedListing.EscrowTransactions.First(e => e.Id != expiredEscrow.Id);
        Assert.Equal(buyer.Id, newEscrow.BuyerId);
        Assert.Equal(EscrowStatus.Pending, newEscrow.Status);
    }

    [Fact]
    public async Task Handle_WhenListingHasRefundQueuedEscrow_ShouldCreateNewEscrowAndPreserveRefundQueuedRecord()
    {
        // Arrange
        var (dbContext, seller, buyer) = CreateInMemoryDbContext();
        var previousBuyerId = Guid.NewGuid();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer.Id);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-HOLD-REFUND-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified
        };

        // Escrow of previous buyer who transferred late -> RefundQueued with bank transaction reference
        var refundQueuedEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = previousBuyerId,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_000_000m,
            TotalBuyerPaid = 1_050_000m,
            PaymentReference = "TSREFUND99",
            BankTransactionReference = "SEPAY_TX_987654321",
            Status = EscrowStatus.RefundQueued,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        listing.EscrowTransactions.Add(refundQueuedEscrow);
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listing.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);

        // 1. Critical Verification: The RefundQueued record must NOT be overwritten
        var dbRefundEscrow = await dbContext.EscrowTransactions.FindAsync(refundQueuedEscrow.Id);
        Assert.NotNull(dbRefundEscrow);
        Assert.Equal(EscrowStatus.RefundQueued, dbRefundEscrow.Status);
        Assert.Equal("SEPAY_TX_987654321", dbRefundEscrow.BankTransactionReference);
        Assert.Equal(previousBuyerId, dbRefundEscrow.BuyerId);
        Assert.Equal("TSREFUND99", dbRefundEscrow.PaymentReference);

        // 2. A brand new escrow must be created for the new buyer
        var updatedListing = await dbContext.ResaleListings.Include(l => l.EscrowTransactions).FirstAsync(l => l.Id == listing.Id);
        Assert.Equal(2, updatedListing.EscrowTransactions.Count);
        var newEscrow = updatedListing.EscrowTransactions.First(e => e.Id != refundQueuedEscrow.Id);
        Assert.Equal(buyer.Id, newEscrow.BuyerId);
        Assert.Equal(EscrowStatus.Pending, newEscrow.Status);
        Assert.NotEqual("TSREFUND99", newEscrow.PaymentReference);
        Assert.Null(newEscrow.BankTransactionReference);
    }

    [Fact]
    public async Task Handle_WhenListingIsExpired_ShouldThrowBusinessRuleViolationException()
    {
        var (dbContext, seller, buyer) = CreateInMemoryDbContext();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer.Id);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-EXPIRED-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Expired
        };
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listing.Id);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Contains("Expired", ex.Message);
    }

    [Fact]
    public async Task Handle_WhenBuyerNotAuthenticated_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var (dbContext, _, _) = CreateInMemoryDbContext();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(null); // Anonymous / No JWT

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(Guid.NewGuid());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("You must be signed in to reserve tickets for purchase.", ex.Message);
    }

    [Fact]
    public async Task Handle_WhenBuyerNotInShadowUsers_ShouldThrowUnauthorizedException()
    {
        // Arrange: Valid JWT token with UserId, but user is not synced in ShadowUsers table (Auto-Heal removed)
        var (dbContext, _, _) = CreateInMemoryDbContext();
        var feeCalculator = new MockResaleFeeCalculator();
        var unsyncedBuyerId = Guid.NewGuid();
        var currentUserService = new MockCurrentUserService(unsyncedBuyerId, email: null);

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(Guid.NewGuid());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("User account does not exist or has not been synced.", ex.Message);
    }

    [Fact]
    public async Task Handle_WhenBuyerIsInactive_ShouldThrowUnauthorizedException()
    {
        // Arrange: Buyer exists in ShadowUsers but account is deactivated
        var (dbContext, _, buyer) = CreateInMemoryDbContext();
        buyer.IsActive = false;
        await dbContext.SaveChangesAsync();

        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer.Id);

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(Guid.NewGuid());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("Your account has been deactivated.", ex.Message);
    }

    [Fact]
    public async Task Handle_WhenEventInsideTwoHourCutoff_ShouldRejectHoldAndLeaveListingVerified()
    {
        var (dbContext, seller, buyer) = CreateInMemoryDbContext();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer.Id);

        var ev = await dbContext.Events.FirstAsync();
        ev.EventStartAt = DateTimeOffset.UtcNow.AddMinutes(90);
        await dbContext.SaveChangesAsync();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-CUTOFF-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listing.Id);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => handler.Handle(command, CancellationToken.None));

        Assert.Contains("2 hours", ex.Message);
        var saved = await dbContext.ResaleListings.Include(l => l.EscrowTransactions).FirstAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Verified, saved.ListingStatus);
        Assert.Empty(saved.EscrowTransactions);
    }

    [Fact]
    public async Task Handle_WhenListingHasTransferNeedsReviewEscrow_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var (dbContext, seller, buyer) = CreateInMemoryDbContext();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer.Id);

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-HOLD-REV-01",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var reviewEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = Guid.NewGuid(),
            SellerId = seller.Id,
            PaymentReference = "TSREVHOLD1",
            Status = EscrowStatus.TransferNeedsReview
        };
        listing.EscrowTransactions.Add(reviewEscrow);
        dbContext.ResaleListings.Add(listing);
        dbContext.EscrowTransactions.Add(reviewEscrow);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listing.Id);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Contains("undergoing payment processing or reconciliation", ex.Message);
    }

    [Fact]
    public async Task Handle_WhenBundleHasTransferNeedsReviewEscrow_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var (dbContext, seller, buyer) = CreateInMemoryDbContext();
        var feeCalculator = new MockResaleFeeCalculator();
        var currentUserService = new MockCurrentUserService(buyer.Id);
        var bundleId = Guid.NewGuid();

        var listing1 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-BND-REV-01",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            IsBundleAllOrNothing = true,
            BundleTotalTickets = 2
        };
        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-BND-REV-02",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            IsBundleAllOrNothing = true,
            BundleTotalTickets = 2
        };
        var bundleReviewEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing1.Id,
            BundleId = bundleId,
            BuyerId = Guid.NewGuid(),
            SellerId = seller.Id,
            PaymentReference = "TSREVBUNDLEHOLD1",
            Status = EscrowStatus.TransferNeedsReview
        };
        dbContext.ResaleListings.AddRange(listing1, listing2);
        dbContext.EscrowTransactions.Add(bundleReviewEscrow);
        await dbContext.SaveChangesAsync();

        var handler = new HoldListingForPurchaseCommandHandler(dbContext, feeCalculator, currentUserService);
        var command = new HoldListingForPurchaseCommand(listing1.Id);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Contains("undergoing payment processing or reconciliation", ex.Message);
    }
}
