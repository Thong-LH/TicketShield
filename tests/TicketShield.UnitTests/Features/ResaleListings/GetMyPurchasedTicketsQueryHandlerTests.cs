using Microsoft.EntityFrameworkCore;
using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.ResaleListings.Queries.GetMyPurchasedTickets;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class GetMyPurchasedTicketsQueryHandlerTests
{
    private static (TicketShieldDbContext dbContext, Guid buyerId, string buyerEmail) CreateTestFixture()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new TicketShieldDbContext(options);

        var buyerId = Guid.NewGuid();
        var buyerEmail = "buyer@test.com";

        var seller = new ShadowUser { Id = Guid.NewGuid(), Email = "seller@test.com", FullName = "Seller User" };
        var buyer = new ShadowUser { Id = buyerId, Email = buyerEmail, FullName = "Buyer User" };
        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "Test Organizer",
            OfficialEmail = "organizer@test.com"
        };
        var testEvent = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "Anh Trai Say Hi Concert",
            Venue = "My Dinh Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(15),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(15).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(14),
            Organizer = organizer
        };
        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierName = "SVIP",
            OriginalPrice = 1_500_000m,
            Event = testEvent
        };

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "OLD-SELLER-TICKET-001",
            ResalePrice = 1_500_000m,
            ListingStatus = ListingStatus.Sold,
            Event = testEvent,
            Tier = tier,
            Seller = seller
        };

        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyerId,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_500_000m,
            BuyerFee = 50_000m,
            SellerFee = 50_000m,
            TotalBuyerPaid = 1_550_000m,
            NetSellerPayout = 1_450_000m,
            Status = EscrowStatus.Locked,
            PaymentReference = "TSBUYERPASS1",
            RecipientEmail = buyerEmail,
            RecipientName = "Buyer User",
            NewTicketCode = "BTC-AUTHENTIC-PASS-777",
            QrCodeData = "https://organizer.ticket/authentic-pass-777",
            Listing = listing,
            Buyer = buyer,
            Seller = seller
        };

        context.ShadowUsers.AddRange(seller, buyer);
        context.Organizers.Add(organizer);
        context.Events.Add(testEvent);
        context.TicketTiers.Add(tier);
        context.ResaleListings.Add(listing);
        context.EscrowTransactions.Add(escrow);
        context.SaveChanges();

        return (context, buyerId, buyerEmail);
    }

    [Fact]
    public async Task Handle_WhenUnauthenticated_ShouldThrowUnauthorizedException()
    {
        var (context, _, _) = CreateTestFixture();
        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.Setup(u => u.UserId).Returns((Guid?)null);
        currentUserService.Setup(u => u.Email).Returns((string?)null);

        var handler = new GetMyPurchasedTicketsQueryHandler(context, currentUserService.Object);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new GetMyPurchasedTicketsQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenAuthenticated_ShouldReturnAuthenticBtcTicketCodeAndQrPayload()
    {
        var (context, buyerId, buyerEmail) = CreateTestFixture();
        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.Setup(u => u.UserId).Returns(buyerId);
        currentUserService.Setup(u => u.Email).Returns(buyerEmail);

        var handler = new GetMyPurchasedTicketsQueryHandler(context, currentUserService.Object);

        var result = await handler.Handle(new GetMyPurchasedTicketsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data!);

        var ticket = result.Data!.First();
        Assert.Equal("BTC-AUTHENTIC-PASS-777", ticket.TicketPassCode);
        Assert.Equal("https://organizer.ticket/authentic-pass-777", ticket.QrCodeData);
        Assert.DoesNotContain("OLD-SELLER-TICKET-001", ticket.TicketPassCode);
        Assert.DoesNotContain("OLD-SELLER-TICKET-001", ticket.QrCodeData);
        Assert.DoesNotContain("OLD-SELLER-TICKET-001", ticket.QrCodeImageUrl);
        Assert.DoesNotContain("TICKETSHIELD:OFFICIAL_PASS", ticket.QrCodeData);
        Assert.Equal("IN_ESCROW", ticket.Status);
    }

    [Fact]
    public async Task Handle_WhenEscrowIsPendingHold_ShouldNotReturnItAsPurchasedTicket()
    {
        var (context, buyerId, buyerEmail) = CreateTestFixture();
        var paid = context.EscrowTransactions.Single();
        paid.Status = EscrowStatus.Pending;
        paid.NewTicketCode = null;
        paid.QrCodeData = null;
        context.SaveChanges();

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.Setup(u => u.UserId).Returns(buyerId);
        currentUserService.Setup(u => u.Email).Returns(buyerEmail);

        var handler = new GetMyPurchasedTicketsQueryHandler(context, currentUserService.Object);
        var result = await handler.Handle(new GetMyPurchasedTicketsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task Handle_WhenPendingHoldExistsBesidePaidTicket_ShouldNotLeakSellerOriginalTicketCode()
    {
        var (context, buyerId, buyerEmail) = CreateTestFixture();
        var listing = context.ResaleListings.Single();
        var paid = context.EscrowTransactions.Single();

        context.EscrowTransactions.Add(new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyerId,
            SellerId = listing.SellerId,
            OriginalTicketPrice = 1_500_000m,
            BuyerFee = 50_000m,
            SellerFee = 50_000m,
            TotalBuyerPaid = 1_550_000m,
            NetSellerPayout = 1_450_000m,
            Status = EscrowStatus.Pending,
            PaymentReference = "TSPENDHOLD1",
            RecipientEmail = buyerEmail,
            RecipientName = "Buyer User",
            NewTicketCode = null,
            QrCodeData = null,
            Listing = listing
        });
        context.SaveChanges();

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.Setup(u => u.UserId).Returns(buyerId);
        currentUserService.Setup(u => u.Email).Returns(buyerEmail);

        var handler = new GetMyPurchasedTicketsQueryHandler(context, currentUserService.Object);
        var result = await handler.Handle(new GetMyPurchasedTicketsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data!);
        Assert.Equal(paid.Id, result.Data![0].EscrowId);
        Assert.All(result.Data, ticket =>
        {
            Assert.NotEqual("PENDING_PAYMENT", ticket.Status);
            Assert.DoesNotContain("OLD-SELLER-TICKET-001", ticket.TicketPassCode);
            Assert.DoesNotContain("OLD-SELLER-TICKET-001", ticket.QrCodeData);
            Assert.DoesNotContain("OLD-SELLER-TICKET-001", ticket.QrCodeImageUrl);
        });
    }

    [Fact]
    public async Task Handle_WhenPaidButOrganizerCodeMissing_ShouldNotFallbackToSellerOriginalTicketCode()
    {
        var (context, buyerId, buyerEmail) = CreateTestFixture();
        var paid = context.EscrowTransactions.Single();
        paid.NewTicketCode = null;
        paid.QrCodeData = null;
        context.SaveChanges();

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.Setup(u => u.UserId).Returns(buyerId);
        currentUserService.Setup(u => u.Email).Returns(buyerEmail);

        var handler = new GetMyPurchasedTicketsQueryHandler(context, currentUserService.Object);
        var result = await handler.Handle(new GetMyPurchasedTicketsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data!);
        var ticket = result.Data![0];
        Assert.Equal("IN_ESCROW", ticket.Status);
        Assert.True(string.IsNullOrWhiteSpace(ticket.TicketPassCode));
        Assert.True(string.IsNullOrWhiteSpace(ticket.QrCodeData));
        Assert.DoesNotContain("OLD-SELLER-TICKET-001", ticket.TicketPassCode ?? string.Empty);
        Assert.DoesNotContain("OLD-SELLER-TICKET-001", ticket.QrCodeData ?? string.Empty);
        Assert.DoesNotContain("OLD-SELLER-TICKET-001", ticket.QrCodeImageUrl ?? string.Empty);
    }

    [Theory]
    [InlineData(EscrowStatus.Refunded)]
    [InlineData(EscrowStatus.RefundQueued)]
    public async Task Handle_WhenEscrowIsRefundState_ShouldNotReturnAsPurchasedTicket(EscrowStatus refundStatus)
    {
        var (context, buyerId, buyerEmail) = CreateTestFixture();
        var paid = context.EscrowTransactions.Single();
        paid.Status = refundStatus;
        paid.NewTicketCode = null;
        paid.QrCodeData = null;
        context.SaveChanges();

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.Setup(u => u.UserId).Returns(buyerId);
        currentUserService.Setup(u => u.Email).Returns(buyerEmail);

        var handler = new GetMyPurchasedTicketsQueryHandler(context, currentUserService.Object);
        var result = await handler.Handle(new GetMyPurchasedTicketsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!);
    }
}

