using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class ProcessSePayWebhookCommandHandlerTests
{
    private static (TicketShieldDbContext dbContext, ResaleListing listing, EscrowTransaction escrow) CreateTestFixture(
        string paymentReference = "TS1A2B3C4D",
        decimal totalBuyerPaid = 550_000m,
        EscrowStatus escrowStatus = EscrowStatus.Pending,
        ListingStatus listingStatus = ListingStatus.Transacting)
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new TicketShieldDbContext(options);

        var seller = new ShadowUser { Id = Guid.NewGuid(), Email = "seller@test.com", FullName = "Seller User" };
        var buyer = new ShadowUser { Id = Guid.NewGuid(), Email = "buyer@test.com", FullName = "Buyer User" };
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
            Name = "Test Concert",
            Venue = "Test Venue",
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
            OriginalPrice = 500_000m,
            Event = testEvent
        };

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK12345",
            OriginalPrice = 500_000m,
            ResalePrice = 500_000m,
            ListingStatus = listingStatus,
            Event = testEvent,
            Tier = tier
        };

        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 500_000m,
            BuyerFee = 50_000m,
            SellerFee = 25_000m,
            TotalBuyerPaid = totalBuyerPaid,
            NetSellerPayout = 475_000m,
            PaymentReference = paymentReference,
            Status = escrowStatus,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        listing.EscrowTransaction = escrow;

        context.Organizers.Add(organizer);
        context.Events.Add(testEvent);
        context.TicketTiers.Add(tier);
        context.ShadowUsers.AddRange(seller, buyer);
        context.ResaleListings.Add(listing);
        context.EscrowTransactions.Add(escrow);
        context.SaveChanges();

        return (context, listing, escrow);
    }

    [Fact]
    public async Task Handle_ValidPaymentWebhook_ShouldLockEscrowAndMarkListingSold()
    {
        // Arrange
        var (context, listing, escrow) = CreateTestFixture("TS1A2B3C4D", 550_000m);
        var handler = new ProcessSePayWebhookCommandHandler(context);

        var request = new SePayWebhookRequest
        {
            Id = 10001,
            Gateway = "MBBank",
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D thanh toan mua ve TicketShield",
            ReferenceCode = "FT262615291234"
        };

        // Act
        var result = await handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Locked", result.Data.EscrowStatus);
        Assert.Equal("Sold", result.Data.ListingStatus);
        Assert.False(result.Data.IsIdempotentDuplicate);
        Assert.Equal("FT262615291234", result.Data.BankTransactionReference);

        var dbEscrow = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.Equal(EscrowStatus.Locked, dbEscrow!.Status);
        Assert.Equal("FT262615291234", dbEscrow.BankTransactionReference);
        Assert.True(dbEscrow.InSettlementBuffer);

        var dbListing = await context.ResaleListings.FindAsync(listing.Id);
        Assert.Equal(ListingStatus.Sold, dbListing!.ListingStatus);
    }

    [Fact]
    public async Task Handle_OutboundTransfer_ShouldIgnoreAndReturnIgnoredStatus()
    {
        // Arrange
        var (context, _, _) = CreateTestFixture();
        var handler = new ProcessSePayWebhookCommandHandler(context);

        var request = new SePayWebhookRequest
        {
            Id = 10002,
            TransferType = "out",
            TransferAmount = 100_000m,
            Content = "Rut tien bank"
        };

        // Act
        var result = await handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Ignored", result.Data.EscrowStatus);
        Assert.False(result.Data.IsIdempotentDuplicate);
    }

    [Fact]
    public async Task Handle_DuplicateWebhookReferenceCode_ShouldReturnIdempotentSuccess()
    {
        // Arrange
        var (context, _, escrow) = CreateTestFixture("TS88888888", 550_000m);
        escrow.Status = EscrowStatus.Locked;
        escrow.BankTransactionReference = "FTEXISTING123";
        await context.SaveChangesAsync();

        var handler = new ProcessSePayWebhookCommandHandler(context);

        var request = new SePayWebhookRequest
        {
            Id = 10003,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS88888888 thanh toan trung lap",
            ReferenceCode = "FTEXISTING123"
        };

        // Act
        var result = await handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.Data.IsIdempotentDuplicate);
        Assert.Equal("Locked", result.Data.EscrowStatus);
    }

    [Fact]
    public async Task Handle_InsufficientPaymentAmount_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var (context, _, _) = CreateTestFixture("TS99999999", 550_000m);
        var handler = new ProcessSePayWebhookCommandHandler(context);

        var request = new SePayWebhookRequest
        {
            Id = 10004,
            TransferType = "in",
            TransferAmount = 500_000m, // Paid less than 550,000 TotalBuyerPaid
            Content = "TS99999999 chuyen thieu tien",
            ReferenceCode = "FTFEW001"
        };

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_MissingOrInvalidPaymentReference_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var (context, _, _) = CreateTestFixture();
        var handler = new ProcessSePayWebhookCommandHandler(context);

        var request = new SePayWebhookRequest
        {
            Id = 10005,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "Chuyen tien khong co ma TS",
            Code = null
        };

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NonExistentPaymentReference_ShouldThrowNotFoundException()
    {
        // Arrange
        var (context, _, _) = CreateTestFixture();
        var handler = new ProcessSePayWebhookCommandHandler(context);

        var request = new SePayWebhookRequest
        {
            Id = 10006,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TSNOTFOUND01 thanh toan ma khong ton tai"
        };

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenHoldExpired_ShouldNotMarkSold()
    {
        var (context, listing, escrow) = CreateTestFixture();
        listing.ListingStatus = ListingStatus.Verified;
        escrow.UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        var handler = new ProcessSePayWebhookCommandHandler(context);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
            {
                Id = 10007,
                TransferType = "in",
                TransferAmount = 550_000m,
                Content = "TS1A2B3C4D thanh toan tre",
                ReferenceCode = "FTLATE001"
            }), CancellationToken.None));

        Assert.Equal(EscrowStatus.Pending, (await context.EscrowTransactions.FindAsync(escrow.Id))!.Status);
        Assert.Equal(ListingStatus.Verified, (await context.ResaleListings.FindAsync(listing.Id))!.ListingStatus);
    }

    [Fact]
    public async Task Handle_ValidPaymentWebhook_ShouldOverwriteUnlockAtAndSetSettlementBuffer()
    {
        var (context, listing, escrow) = CreateTestFixture("TS1A2B3C4D", 550_000m);
        var holdExpiry = escrow.UnlockAt;
        var handler = new ProcessSePayWebhookCommandHandler(context);
        var before = DateTimeOffset.UtcNow;

        var result = await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10001,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D thanh toan ve TicketShield",
            ReferenceCode = "FT262615291234"
        }), CancellationToken.None);

        var dbEscrow = await context.EscrowTransactions.FindAsync(escrow.Id);
        var expected = EscrowTransaction.ComputeSettlementUnlockAt(before, listing.Event.EventStartAt);
        Assert.True(result.Success);
        Assert.Equal(EscrowStatus.Locked, dbEscrow!.Status);
        Assert.True(dbEscrow.InSettlementBuffer);
        Assert.NotEqual(holdExpiry, dbEscrow.UnlockAt);
        Assert.True(dbEscrow.UnlockAt >= expected.AddSeconds(-2));
        Assert.True(dbEscrow.UnlockAt <= expected.AddSeconds(2));
        Assert.Equal(ListingStatus.Sold, (await context.ResaleListings.FindAsync(listing.Id))!.ListingStatus);
    }

    [Fact]
    public void ComputeSettlementUnlockAt_UsesMinOf24hAndEventMinus2h()
    {
        var now = new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);
        var eventStart = now.AddDays(10);
        var actual = EscrowTransaction.ComputeSettlementUnlockAt(now, eventStart);
        Assert.Equal(now.AddHours(24), actual);

        var nearEvent = now.AddHours(5);
        var near = EscrowTransaction.ComputeSettlementUnlockAt(now, nearEvent);
        Assert.Equal(nearEvent.AddHours(-2), near);
    }
}
