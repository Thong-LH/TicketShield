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
    private static (TicketShieldDbContext dbContext, User seller1, User seller2) CreateInMemoryDbContext()
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
        var seller1 = new User
        {
            Id = Guid.NewGuid(),
            Email = "seller1@ticketshield.vn",
            FullName = "Nguyen Van Seller One",
            Role = UserRole.User
        };
        var seller2 = new User
        {
            Id = Guid.NewGuid(),
            Email = "seller2@ticketshield.vn",
            FullName = "Le Van Seller Two",
            Role = UserRole.User
        };

        context.Organizers.Add(organizer);
        context.Events.Add(testEvent);
        context.TicketTiers.Add(tier);
        context.Users.AddRange(seller1, seller2);
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
        public Task<TicketShield.Application.Resale.VerificationResult> Cancel(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task CancelByListingId(string seller, Guid listingId, string key, CancellationToken ct)
        {
            throw new InvalidOperationException("Failed to reach MockOrganizer gateway to unlock ticket.");
        }
<<<<<<< Updated upstream
        public Task<List<TicketShield.Application.Resale.ListingResult>> Marketplace(int page, int size, CancellationToken ct) => throw new NotImplementedException();
=======

        public Task<List<ListingResult>> Marketplace(int page, int size, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Request(string seller, string key, string ticket, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Resend(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Confirm(string seller, string id, string key, string otp, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Get(string seller, string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Close(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Publish(string seller, string key, PublishBody body, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Cancel(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
>>>>>>> Stashed changes
        public Task RecoverPending(CancellationToken ct) => throw new NotImplementedException();
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
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command, CancellationToken.None));

        // Verify database listing status remains Verified (not Cancelled)
        var dbListing = await dbContext.ResaleListings.FindAsync(listing.Id);
        Assert.NotNull(dbListing);
        Assert.Equal(ListingStatus.Verified, dbListing.ListingStatus);
    }
}
