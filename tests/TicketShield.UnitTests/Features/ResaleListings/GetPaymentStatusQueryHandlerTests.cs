using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.ResaleListings.Queries.GetPaymentStatus;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class GetPaymentStatusQueryHandlerTests
{
    private sealed class MockCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; }
        public string? Email => "buyer@test.com";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;

        public MockCurrentUserService(Guid? userId) => UserId = userId;
    }

    private static (TicketShieldDbContext db, ResaleListing listing, EscrowTransaction escrow, ShadowUser buyer) CreateFixture()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new TicketShieldDbContext(options);

        var seller = new ShadowUser { Id = Guid.NewGuid(), Email = "seller@test.com", FullName = "Seller" };
        var buyer = new ShadowUser { Id = Guid.NewGuid(), Email = "buyer@test.com", FullName = "Buyer" };
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            SellerId = seller.Id,
            OriginalTicketCode = "TCK1",
            OriginalPrice = 500_000m,
            ResalePrice = 500_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 500_000m,
            TotalBuyerPaid = 550_000m,
            NetSellerPayout = 475_000m,
            PaymentReference = "TS1A2B3C4D",
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(8),
            InSettlementBuffer = false
        };
        listing.EscrowTransaction = escrow;
        context.ShadowUsers.AddRange(seller, buyer);
        context.ResaleListings.Add(listing);
        context.EscrowTransactions.Add(escrow);
        context.SaveChanges();
        return (context, listing, escrow, buyer);
    }

    [Fact]
    public async Task Handle_BuyerOfEscrow_ShouldReturnPaymentStatus()
    {
        var (db, listing, escrow, buyer) = CreateFixture();
        var handler = new GetPaymentStatusQueryHandler(db, new MockCurrentUserService(buyer.Id));

        var result = await handler.Handle(new GetPaymentStatusQuery(listing.Id), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(listing.Id, result.Data!.ListingId);
        Assert.Equal(escrow.Id, result.Data.EscrowId);
        Assert.Equal("Transacting", result.Data.ListingStatus);
        Assert.Equal("Pending", result.Data.EscrowStatus);
        Assert.Equal("TS1A2B3C4D", result.Data.PaymentReference);
        Assert.False(result.Data.InSettlementBuffer);
    }

    [Fact]
    public async Task Handle_OtherUser_ShouldThrowForbidden()
    {
        var (db, listing, _, _) = CreateFixture();
        var handler = new GetPaymentStatusQueryHandler(db, new MockCurrentUserService(Guid.NewGuid()));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new GetPaymentStatusQuery(listing.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_MissingEscrow_ShouldThrowNotFound()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TicketShieldDbContext(options);
        var seller = new ShadowUser { Id = Guid.NewGuid(), Email = "s@test.com", FullName = "S" };
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            SellerId = seller.Id,
            OriginalTicketCode = "TCK2",
            OriginalPrice = 1,
            ResalePrice = 1,
            ListingStatus = ListingStatus.Verified
        };
        db.ShadowUsers.Add(seller);
        db.ResaleListings.Add(listing);
        db.SaveChanges();

        var handler = new GetPaymentStatusQueryHandler(db, new MockCurrentUserService(seller.Id));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetPaymentStatusQuery(listing.Id), CancellationToken.None));
    }
}
