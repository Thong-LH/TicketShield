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

        var updatedListing = await dbContext.ResaleListings.Include(l => l.EscrowTransaction).FirstAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Transacting, updatedListing.ListingStatus);
        Assert.NotNull(updatedListing.EscrowTransaction);
        Assert.Equal(buyer.Id, updatedListing.EscrowTransaction.BuyerId);
        Assert.Equal(EscrowStatus.Pending, updatedListing.EscrowTransaction.Status);
    }

    [Fact]
    public async Task Handle_WhenBuyerIsSeller_ShouldThrowBusinessRuleViolationException()
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
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(command, CancellationToken.None));
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
        Assert.Equal(buyer.Id, expiredEscrow.BuyerId); // Replaced buyer ID
        Assert.NotEqual("TSEXPIRED1", result.Data.PaymentReference); // New unique payment reference
    }
}
