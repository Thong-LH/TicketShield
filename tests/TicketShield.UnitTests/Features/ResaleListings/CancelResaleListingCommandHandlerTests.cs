using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Resale;
using TicketShield.Application.Features.ResaleListings.Commands.CancelResaleListing;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class CancelResaleListingCommandHandlerTests
{
    private static (TicketShieldDbContext dbContext, ShadowUser seller1, ShadowUser seller2) CreateInMemoryDbContext()
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
        var seller1 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller1@ticketshield.vn",
            FullName = "Nguyen Van Seller One"
        };
        var seller2 = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller2@ticketshield.vn",
            FullName = "Le Van Seller Two"
        };

        context.Organizers.Add(organizer);
        context.Events.Add(testEvent);
        context.TicketTiers.Add(tier);
        context.ShadowUsers.AddRange(seller1, seller2);
        context.SaveChanges();

        return (context, seller1, seller2);
    }

    private class MockCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; }
        public string? Email => "seller@ticketshield.vn";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;

        public MockCurrentUserService(Guid? userId) => UserId = userId;
    }

    [Fact]
    public async Task Handle_WhenListingIsVerifiedAndCallerIsSeller_ShouldChangeStatusToCancelled()
    {
        // Arrange
        var (dbContext, seller1, _) = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-CANCEL-001",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_800_000m,
            ListingStatus = ListingStatus.Verified,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        var currentUserService = new MockCurrentUserService(seller1.Id);
        var handler = new CancelResaleListingCommandHandler(dbContext, currentUserService);
        var command = new CancelResaleListingCommand(listing.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("Cancelled", result.Data.ListingStatus);
        Assert.Equal("TCK-CANCEL-001", result.Data.OriginalTicketCode);
        Assert.Equal(new[] { "TCK-CANCEL-001" }, result.Data.AllCancelledTicketCodes);

        var dbListing = await dbContext.ResaleListings.FindAsync(listing.Id);
        Assert.NotNull(dbListing);
        Assert.Equal(ListingStatus.Cancelled, dbListing.ListingStatus);
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSeller_ShouldThrowForbiddenAccessException()
    {
        // Arrange
        var (dbContext, seller1, seller2) = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-CANCEL-002",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_800_000m,
            ListingStatus = ListingStatus.Verified
        };
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        // Seller 2 tries to cancel Seller 1's listing
        var currentUserService = new MockCurrentUserService(seller2.Id);
        var handler = new CancelResaleListingCommandHandler(dbContext, currentUserService);
        var command = new CancelResaleListingCommand(listing.Id);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenListingIsNotVerified_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var (dbContext, seller1, _) = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();

        var soldListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-SOLD-003",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_800_000m,
            ListingStatus = ListingStatus.Sold
        };
        dbContext.ResaleListings.Add(soldListing);
        await dbContext.SaveChangesAsync();

        var currentUserService = new MockCurrentUserService(seller1.Id);
        var handler = new CancelResaleListingCommandHandler(dbContext, currentUserService);
        var command = new CancelResaleListingCommand(soldListing.Id);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => handler.Handle(command, CancellationToken.None));
        Assert.Contains("Chỉ có thể hủy tin đăng khi vé chưa bị người mua đặt hoặc mua", ex.Message);
    }

    [Fact]
    public async Task Handle_WhenListingNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var (dbContext, seller1, _) = CreateInMemoryDbContext();
        var currentUserService = new MockCurrentUserService(seller1.Id);
        var handler = new CancelResaleListingCommandHandler(dbContext, currentUserService);
        var command = new CancelResaleListingCommand(Guid.NewGuid());

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None));
    }

    private class MockFailingVerificationService : ITicketVerificationService
    {
        public Task<TicketShield.Application.Resale.VerificationResult> Request(string seller, string key, string ticket, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Resend(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Confirm(string seller, string id, string key, string otp, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Get(string seller, string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Close(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Publish(string seller, string key, TicketShield.Application.Resale.PublishBody body, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> PublishBundleItem(
            string seller, string key, TicketShield.Application.Resale.PublishBody body, Guid bundleId, int bundleTotalTickets, bool allOrNothing, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Cancel(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task CancelByListingId(string seller, Guid listingId, string key, CancellationToken ct)
        {
            throw new InvalidOperationException("Failed to reach MockOrganizer gateway to unlock ticket.");
        }
        public Task<List<TicketShield.Application.Resale.ListingResult>> Marketplace(int page, int size, CancellationToken ct) => throw new NotImplementedException();
        public Task RecoverPending(CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Contracts.Organizer.V1.TransferOwnershipResponse> TransferOwnership(
            string seller, string verificationId, string lockId, ulong expectedLockGeneration, string buyerRef, string buyerEmail, string buyerName, string? buyerPhone, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Contracts.Organizer.V1.TransferOwnershipResponse> TransferOwnershipByListingId(
            Guid listingId, Guid buyerId, string buyerEmail, string buyerName, string? buyerPhone, CancellationToken ct) => throw new NotImplementedException();
    }


    [Fact]
    public async Task Handle_WhenVerificationServiceUnlockFails_ShouldThrowAndNotCancelInDb()
    {
        // Arrange
        var (dbContext, seller1, _) = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-UNLOCK-FAIL-009",
            OriginalPrice = 2_000_000m,
            ResalePrice = 1_800_000m,
            ListingStatus = ListingStatus.Verified
        };
        dbContext.ResaleListings.Add(listing);
        await dbContext.SaveChangesAsync();

        var currentUserService = new MockCurrentUserService(seller1.Id);
        var failingService = new MockFailingVerificationService();
        var handler = new CancelResaleListingCommandHandler(dbContext, currentUserService, failingService);
        var command = new CancelResaleListingCommand(listing.Id);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => handler.Handle(command, CancellationToken.None));
        Assert.Contains("Không thể mở khóa trọn gói vé với Ban Tổ Chức (đã mở khóa 0/1 vé)", ex.Message);

        // Verify database listing status remains Verified (not Cancelled)
        var dbListing = await dbContext.ResaleListings.FindAsync(listing.Id);
        Assert.NotNull(dbListing);
        Assert.Equal(ListingStatus.Verified, dbListing.ListingStatus);
    }

    [Fact]
    public async Task Handle_WhenListingIsBundle_ShouldCancelAllSiblingsInBundle()
    {
        // Arrange
        var (dbContext, seller1, _) = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();
        var bundleId = Guid.NewGuid();

        var listing1 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-BUNDLE-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 900_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = bundleId,
            BundleTotalTickets = 2,
            IsBundleAllOrNothing = true,
            CreatedAt = DateTimeOffset.Parse("2026-10-02T00:00:00Z")
        };
        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-BUNDLE-002",
            OriginalPrice = 1_000_000m,
            ResalePrice = 900_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = bundleId,
            BundleTotalTickets = 2,
            IsBundleAllOrNothing = true,
            CreatedAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z")
        };
        dbContext.ResaleListings.AddRange(listing1, listing2);
        await dbContext.SaveChangesAsync();

        var currentUserService = new MockCurrentUserService(seller1.Id);
        var handler = new CancelResaleListingCommandHandler(dbContext, currentUserService, null);
        var command = new CancelResaleListingCommand(listing1.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var updatedListing1 = await dbContext.ResaleListings.FindAsync(listing1.Id);
        var updatedListing2 = await dbContext.ResaleListings.FindAsync(listing2.Id);

        Assert.NotNull(updatedListing1);
        Assert.NotNull(updatedListing2);
        Assert.Equal(ListingStatus.Cancelled, updatedListing1.ListingStatus);
        Assert.Equal(ListingStatus.Cancelled, updatedListing2.ListingStatus);
        Assert.Equal("TCK-BUNDLE-001", result.Data!.OriginalTicketCode);
        Assert.Equal(new[] { "TCK-BUNDLE-002", "TCK-BUNDLE-001" }, result.Data.AllCancelledTicketCodes);
        Assert.Equal(2, result.Data.AllCancelledTicketCodes.Distinct().Count());
    }

    [Fact]
    public async Task Handle_WhenBundleSiblingAlreadyCancelled_ShouldOmitThatCode()
    {
        var (dbContext, seller1, _) = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();
        var bundleId = Guid.NewGuid();
        var stillListed = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-BUNDLE-OPEN",
            OriginalPrice = 1_000_000m,
            ResalePrice = 900_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = bundleId,
            BundleTotalTickets = 2,
            CreatedAt = DateTimeOffset.Parse("2026-10-02T00:00:00Z")
        };
        var alreadyCancelled = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-BUNDLE-DONE",
            OriginalPrice = 1_000_000m,
            ResalePrice = 900_000m,
            ListingStatus = ListingStatus.Cancelled,
            BundleId = bundleId,
            BundleTotalTickets = 2,
            CreatedAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z")
        };
        dbContext.ResaleListings.AddRange(stillListed, alreadyCancelled);
        await dbContext.SaveChangesAsync();

        var handler = new CancelResaleListingCommandHandler(dbContext, new MockCurrentUserService(seller1.Id), null);
        var result = await handler.Handle(new CancelResaleListingCommand(stillListed.Id), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("TCK-BUNDLE-OPEN", result.Data!.OriginalTicketCode);
        Assert.Equal(new[] { "TCK-BUNDLE-OPEN" }, result.Data.AllCancelledTicketCodes);
        Assert.Equal(ListingStatus.Cancelled, (await dbContext.ResaleListings.FindAsync(alreadyCancelled.Id))!.ListingStatus);
    }

    [Theory]
    [InlineData(ListingStatus.Transacting)]
    [InlineData(ListingStatus.Sold)]
    public async Task Handle_WhenBundleSiblingIsBeingPurchasedOrSold_ShouldRejectAndSkipUnlock(ListingStatus siblingStatus)
    {
        var (dbContext, seller1, _) = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();
        var bundleId = Guid.NewGuid();
        var stillListed = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-BUNDLE-OPEN",
            OriginalPrice = 1_000_000m,
            ResalePrice = 900_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = bundleId,
            BundleTotalTickets = 2
        };
        var blocked = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-BUNDLE-BLOCKED",
            OriginalPrice = 1_000_000m,
            ResalePrice = 900_000m,
            ListingStatus = siblingStatus,
            BundleId = bundleId,
            BundleTotalTickets = 2
        };
        dbContext.ResaleListings.AddRange(stillListed, blocked);
        await dbContext.SaveChangesAsync();

        var unlocks = new CountingVerificationService();
        var handler = new CancelResaleListingCommandHandler(dbContext, new MockCurrentUserService(seller1.Id), unlocks);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => handler.Handle(new CancelResaleListingCommand(stillListed.Id), CancellationToken.None));

        Assert.Contains("Không thể hủy gói vé", ex.Message);
        Assert.Equal(0, unlocks.CancelCalls);
        Assert.Equal(ListingStatus.Verified, (await dbContext.ResaleListings.FindAsync(stillListed.Id))!.ListingStatus);
        Assert.Equal(siblingStatus, (await dbContext.ResaleListings.FindAsync(blocked.Id))!.ListingStatus);
    }

    [Fact]
    public async Task Handle_WhenSecondBundleUnlockFails_ShouldLeaveBothVerified()
    {
        var (dbContext, seller1, _) = CreateInMemoryDbContext();
        var testEvent = await dbContext.Events.FirstAsync();
        var testTier = await dbContext.TicketTiers.FirstAsync();
        var bundleId = Guid.NewGuid();
        var first = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-BUNDLE-FAIL-1",
            OriginalPrice = 1_000_000m,
            ResalePrice = 900_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = bundleId,
            BundleTotalTickets = 2
        };
        var second = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = testTier.Id,
            SellerId = seller1.Id,
            OriginalTicketCode = "TCK-BUNDLE-FAIL-2",
            OriginalPrice = 1_000_000m,
            ResalePrice = 900_000m,
            ListingStatus = ListingStatus.Verified,
            BundleId = bundleId,
            BundleTotalTickets = 2
        };
        dbContext.ResaleListings.AddRange(first, second);
        await dbContext.SaveChangesAsync();

        var unlocks = new CountingVerificationService(failOnCallNumber: 2);
        var handler = new CancelResaleListingCommandHandler(dbContext, new MockCurrentUserService(seller1.Id), unlocks);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => handler.Handle(new CancelResaleListingCommand(first.Id), CancellationToken.None));
        Assert.Contains("Không thể mở khóa trọn gói vé với Ban Tổ Chức (đã mở khóa 1/2 vé)", ex.Message);

        Assert.Equal(2, unlocks.CancelCalls);
        Assert.Equal(ListingStatus.Verified, (await dbContext.ResaleListings.FindAsync(first.Id))!.ListingStatus);
        Assert.Equal(ListingStatus.Verified, (await dbContext.ResaleListings.FindAsync(second.Id))!.ListingStatus);
    }

    private sealed class CountingVerificationService : ITicketVerificationService
    {
        private readonly int _failOnCallNumber;

        public CountingVerificationService(int failOnCallNumber = 0) => _failOnCallNumber = failOnCallNumber;

        public int CancelCalls { get; private set; }

        public Task CancelByListingId(string seller, Guid listingId, string key, CancellationToken ct)
        {
            CancelCalls++;
            if (_failOnCallNumber > 0 && CancelCalls == _failOnCallNumber)
            {
                throw new InvalidOperationException("Failed to reach MockOrganizer gateway to unlock ticket.");
            }

            return Task.CompletedTask;
        }

        public Task<TicketShield.Application.Resale.VerificationResult> Request(string seller, string key, string ticket, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Resend(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Confirm(string seller, string id, string key, string otp, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Get(string seller, string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Close(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Publish(string seller, string key, TicketShield.Application.Resale.PublishBody body, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> PublishBundleItem(
            string seller, string key, TicketShield.Application.Resale.PublishBody body, Guid bundleId, int bundleTotalTickets, bool allOrNothing, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Application.Resale.VerificationResult> Cancel(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<TicketShield.Application.Resale.ListingResult>> Marketplace(int page, int size, CancellationToken ct) => throw new NotImplementedException();
        public Task RecoverPending(CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Contracts.Organizer.V1.TransferOwnershipResponse> TransferOwnership(
            string seller, string verificationId, string lockId, ulong expectedLockGeneration, string buyerRef, string buyerEmail, string buyerName, string? buyerPhone, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Contracts.Organizer.V1.TransferOwnershipResponse> TransferOwnershipByListingId(
            Guid listingId, Guid buyerId, string buyerEmail, string buyerName, string? buyerPhone, CancellationToken ct) => throw new NotImplementedException();
    }
}