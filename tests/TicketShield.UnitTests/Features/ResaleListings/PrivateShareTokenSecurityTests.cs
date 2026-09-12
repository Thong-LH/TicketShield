using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.ResaleListings.Commands.CancelResaleListing;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingByPrivateToken;
using TicketShield.Application.Resale;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

/// <summary>
/// SCRUM-38 · TEST-2.5.3 — Unit-level security rules for private share tokens and listing cancellation.
/// These cover cases that are hard to trigger with the real system (e.g. the organizer being down).
/// The end-to-end HTTP tests live in TicketShield.Resale.Tests/PrivateListingSecurityTests.cs.
/// </summary>
public class PrivateShareTokenSecurityTests
{
    private const string ShareToken = "a1b2c3d4e5f67890a1b2c3d4e5f67890";
    private const string TicketCode = "ATSH-GA-999";

    private static (TicketShieldDbContext Db, ResaleListing Listing, User Seller, User OtherUser) CreateDbWithPrivateListing(
        ListingStatus status = ListingStatus.Verified)
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new TicketShieldDbContext(options);

        var organizer = new Organizer { Id = Guid.NewGuid(), Name = "Security Test Organizer", OfficialEmail = "organizer@test.com" };
        var testEvent = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "Security Test Concert",
            Venue = "Van Hanh Mall Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(20),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(20).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(19),
            Organizer = organizer
        };
        var tier = new TicketTier { Id = Guid.NewGuid(), EventId = testEvent.Id, TierName = "GA Standing", OriginalPrice = 1_200_000m, Event = testEvent };
        var seller = new User { Id = Guid.NewGuid(), Email = "private-seller@ticketshield.vn", FullName = "Private Seller", Role = UserRole.User };
        var otherUser = new User { Id = Guid.NewGuid(), Email = "other-user@ticketshield.vn", FullName = "Other User", Role = UserRole.User };
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = TicketCode,
            OriginalPrice = 1_200_000m,
            ResalePrice = 1_000_000m,
            IsPrivate = true,
            PrivateAccessToken = ShareToken,
            ListingStatus = status
        };

        context.Organizers.Add(organizer);
        context.Events.Add(testEvent);
        context.TicketTiers.Add(tier);
        context.Users.AddRange(seller, otherUser);
        context.ResaleListings.Add(listing);
        context.SaveChanges();
        return (context, listing, seller, otherUser);
    }

    private sealed class FakeCurrentUser(Guid? userId) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email => null;
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;
    }

    /// Fake organizer connection: records whether an unlock was attempted and can simulate the organizer being down.
    private sealed class FakeVerificationService(bool failUnlock = false) : ITicketVerificationService
    {
        public int UnlockCalls { get; private set; }

        public Task CancelByListingId(string seller, Guid listingId, string key, CancellationToken ct)
        {
            UnlockCalls++;
            return failUnlock
                ? throw new ResaleWorkflowException("ORGANIZER_UNAVAILABLE", 503)
                : Task.CompletedTask;
        }

        public Task<VerificationResult> Request(string seller, string key, string ticket, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Resend(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Confirm(string seller, string id, string key, string otp, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Get(string seller, string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Close(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Publish(string seller, string key, PublishBody body, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Cancel(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<ListingResult>> Marketplace(int page, int size, CancellationToken ct) => throw new NotImplementedException();
        public Task RecoverPending(CancellationToken ct) => throw new NotImplementedException();
    }

    // ───────────────────────── Share token lookup ─────────────────────────

    [Fact]
    public async Task PrivateLink_WhenTokenDiffersByOneCharacter_ShouldThrowNotFound()
    {
        // Arrange
        var (db, _, _, _) = CreateDbWithPrivateListing();
        var handler = new GetResaleListingByPrivateTokenQueryHandler(db);
        var guessed = ShareToken[..^1] + "1";

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetResaleListingByPrivateTokenQuery(guessed), CancellationToken.None));
    }

    [Fact]
    public async Task PrivateLink_ShouldNeverReturnTheRawTicketCode()
    {
        // Arrange
        var (db, _, _, _) = CreateDbWithPrivateListing();
        var handler = new GetResaleListingByPrivateTokenQueryHandler(db);

        // Act
        var result = await handler.Handle(new GetResaleListingByPrivateTokenQuery(ShareToken), CancellationToken.None);

        // Assert — "ATSH-GA-999" becomes "AT*******99": only the first 2 and last 2 characters stay visible
        Assert.Equal("AT*******99", result.Data!.MaskedTicketCode);
        Assert.NotEqual(TicketCode, result.Data.MaskedTicketCode);
    }

    [Fact]
    public async Task PrivateLink_WhenListingWasCancelled_ShouldStillOpenAndShowCancelledStatus()
    {
        // Arrange — agreed with BE: an old link must explain why the ticket can no longer be bought
        var (db, _, _, _) = CreateDbWithPrivateListing(ListingStatus.Cancelled);
        var handler = new GetResaleListingByPrivateTokenQueryHandler(db);

        // Act
        var result = await handler.Handle(new GetResaleListingByPrivateTokenQuery(ShareToken), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Cancelled", result.Data!.ListingStatus);
    }

    // ───────────────────────── Cancel ─────────────────────────

    [Fact]
    public async Task Cancel_WhenCallerIsNotTheSeller_ShouldThrowForbiddenAndNeverContactTheOrganizer()
    {
        // Arrange
        var (db, listing, _, otherUser) = CreateDbWithPrivateListing();
        var organizer = new FakeVerificationService();
        var handler = new CancelResaleListingCommandHandler(db, new FakeCurrentUser(otherUser.Id), organizer);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => handler.Handle(new CancelResaleListingCommand(listing.Id), CancellationToken.None));
        Assert.Equal(0, organizer.UnlockCalls);
        Assert.Equal(ListingStatus.Verified, (await db.ResaleListings.SingleAsync()).ListingStatus);
    }

    [Fact]
    public async Task Cancel_WhenOrganizerUnlockFails_ShouldKeepListingOnSaleAndTheShareLinkWorking()
    {
        // Arrange
        var (db, listing, seller, _) = CreateDbWithPrivateListing();
        var organizer = new FakeVerificationService(failUnlock: true);
        var handler = new CancelResaleListingCommandHandler(db, new FakeCurrentUser(seller.Id), organizer);

        // Act
        var error = await Assert.ThrowsAsync<ResaleWorkflowException>(
            () => handler.Handle(new CancelResaleListingCommand(listing.Id), CancellationToken.None));

        // Assert — no half-cancel: the listing is untouched and buyers with the link still see it on sale
        Assert.Equal("ORGANIZER_UNAVAILABLE", error.Code);
        Assert.Equal(1, organizer.UnlockCalls);
        db.ChangeTracker.Clear();
        Assert.Equal(ListingStatus.Verified, (await db.ResaleListings.SingleAsync()).ListingStatus);
        var link = await new GetResaleListingByPrivateTokenQueryHandler(db)
            .Handle(new GetResaleListingByPrivateTokenQuery(ShareToken), CancellationToken.None);
        Assert.Equal("Verified", link.Data!.ListingStatus);
    }

    [Fact]
    public async Task Cancel_WhenSellerCancelsAndUnlockSucceeds_ShouldMarkCancelledAndShowItOnTheShareLink()
    {
        // Arrange
        var (db, listing, seller, _) = CreateDbWithPrivateListing();
        var organizer = new FakeVerificationService();
        var handler = new CancelResaleListingCommandHandler(db, new FakeCurrentUser(seller.Id), organizer);

        // Act
        var result = await handler.Handle(new CancelResaleListingCommand(listing.Id), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, organizer.UnlockCalls);
        var link = await new GetResaleListingByPrivateTokenQueryHandler(db)
            .Handle(new GetResaleListingByPrivateTokenQuery(ShareToken), CancellationToken.None);
        Assert.Equal("Cancelled", link.Data!.ListingStatus);
    }
}
