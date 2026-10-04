using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Payouts.Queries.GetMyPayouts;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;

namespace TicketShield.UnitTests.Features.Payouts;

public class GetMyPayoutsQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenUserIsNotAuthenticated_ThrowsUnauthorized()
    {
        await using var db = CreateDb();
        var handler = new GetMyPayoutsQueryHandler(db, new MockCurrentUserService(null));

        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(new GetMyPayoutsQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTheSignedInSellersPayoutsNewestFirstWithEventName()
    {
        await using var db = CreateDb();
        var sellerId = Guid.NewGuid();
        var otherSellerId = Guid.NewGuid();
        var older = await SeedPayoutAsync(db, sellerId, "PO-OLD", "Concert Cu", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = await SeedPayoutAsync(db, sellerId, "PO-NEW", "Concert Moi", new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
        await SeedPayoutAsync(db, otherSellerId, "PO-OTHER", "Concert Khac", new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero));
        var handler = new GetMyPayoutsQueryHandler(db, new MockCurrentUserService(sellerId));

        var result = await handler.Handle(new GetMyPayoutsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal(newer, result.Data[0].PayoutId);
        Assert.Equal("Concert Moi", result.Data[0].EventName);
        Assert.Equal(older, result.Data[1].PayoutId);
        Assert.Equal("Concert Cu", result.Data[1].EventName);
        Assert.Equal("Success", result.Data[0].Status);
    }

    private static async Task<Guid> SeedPayoutAsync(
        TicketShieldDbContext db,
        Guid sellerId,
        string payoutCode,
        string eventName,
        DateTimeOffset createdAt)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var organizer = new Organizer { Id = Guid.NewGuid(), Name = "Org", OfficialEmail = $"org-{suffix}@test.local" };
        var seller = new ShadowUser { Id = sellerId, Email = $"seller-{suffix}@test.local", FullName = "Seller" };
        if (!await db.ShadowUsers.AnyAsync(user => user.Id == sellerId))
        {
            db.ShadowUsers.Add(seller);
        }

        var buyer = new ShadowUser { Id = Guid.NewGuid(), Email = $"buyer-{suffix}@test.local", FullName = "Buyer" };
        var concert = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = eventName,
            Venue = "Hall",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(10),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(2),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(9)
        };
        var tier = new TicketTier { Id = Guid.NewGuid(), EventId = concert.Id, TierName = "GA", OriginalPrice = 100_000m };
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = concert.Id,
            TierId = tier.Id,
            SellerId = sellerId,
            OriginalTicketCode = $"TCK-{suffix}",
            OriginalPrice = 100_000m,
            ResalePrice = 100_000m,
            ListingStatus = ListingStatus.Sold
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = sellerId,
            Status = EscrowStatus.Released,
            UnlockAt = createdAt.AddHours(24),
            NetSellerPayout = 80_000m
        };
        var payout = new PayoutTransaction
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow.Id,
            SellerId = sellerId,
            PayoutCode = payoutCode,
            Amount = 80_000m,
            Status = PayoutStatus.Success,
            RecipientBankCode = "MB",
            RecipientAccountNumber = "0938434102",
            RecipientAccountName = "SELLER",
            BankReferenceCode = "FT12345678",
            CreatedAt = createdAt
        };
        db.AddRange(organizer, buyer, concert, tier, listing, escrow, payout);
        await db.SaveChangesAsync();
        return payout.Id;
    }

    private static TicketShieldDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    private sealed class MockCurrentUserService : ICurrentUserService
    {
        public MockCurrentUserService(Guid? userId) => UserId = userId;

        public Guid? UserId { get; }
        public string? Email => "seller@test.local";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;
    }
}
