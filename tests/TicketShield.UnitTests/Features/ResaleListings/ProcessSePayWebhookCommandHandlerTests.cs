using Microsoft.EntityFrameworkCore;
using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EscrowBuffer.Models;
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

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            TicketCode = "TCK12345",
            EventId = testEvent.Id,
            TierId = tier.Id,
            CurrentOwnerId = seller.Id,
            OriginalPrice = 500_000m,
            Status = TicketStatus.LockedForResale,
            Event = testEvent,
            Tier = tier
        };

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            TicketId = ticket.Id,
            Ticket = ticket,
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
        context.Tickets.Add(ticket);
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
    public async Task Handle_WhenListingVerifiedButThisHoldStillOpen_ShouldLockEscrow()
    {
        var (context, listing, escrow) = CreateTestFixture();
        listing.ListingStatus = ListingStatus.Verified;
        escrow.UnlockAt = DateTimeOffset.UtcNow.AddMinutes(9);
        await context.SaveChangesAsync();
        var handler = new ProcessSePayWebhookCommandHandler(context);

        var result = await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10017,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D",
            ReferenceCode = "FTRACE001"
        }), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Locked", result.Data.EscrowStatus);
        Assert.Equal(EscrowStatus.Locked, (await context.EscrowTransactions.FindAsync(escrow.Id))!.Status);
        Assert.Equal(ListingStatus.Sold, (await context.ResaleListings.FindAsync(listing.Id))!.ListingStatus);
    }

    [Fact]
    public async Task Handle_WhenHoldExpired_ShouldQueueRefundAndKeepListingUnsold()
    {
        var (context, listing, escrow) = CreateTestFixture();
        listing.ListingStatus = ListingStatus.Verified;
        escrow.UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        var handler = new ProcessSePayWebhookCommandHandler(context);

        var result = await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10007,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D thanh toan tre",
            ReferenceCode = "FTLATE001"
        }), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("RefundQueued", result.Data.EscrowStatus);
        Assert.Equal(EscrowStatus.RefundQueued, (await context.EscrowTransactions.FindAsync(escrow.Id))!.Status);
        Assert.Equal(ListingStatus.Verified, (await context.ResaleListings.FindAsync(listing.Id))!.ListingStatus);
        Assert.False((await context.EscrowTransactions.FindAsync(escrow.Id))!.InSettlementBuffer);
    }

    [Fact]
    public async Task Handle_WhenBuyerCancelledHold_ShouldQueueRefundWithoutLocking()
    {
        var (context, listing, escrow) = CreateTestFixture();
        listing.ListingStatus = ListingStatus.Verified;
        escrow.Status = EscrowStatus.Cancelled;
        escrow.UnlockAt = DateTimeOffset.UtcNow.AddMinutes(8);
        await context.SaveChangesAsync();
        var handler = new ProcessSePayWebhookCommandHandler(context);

        var result = await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10018,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D thanh toan sau khi huy giu cho",
            ReferenceCode = "FTCANCEL001"
        }), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("RefundQueued", result.Data.EscrowStatus);
        Assert.Equal(EscrowStatus.RefundQueued, (await context.EscrowTransactions.FindAsync(escrow.Id))!.Status);
        Assert.Equal(ListingStatus.Verified, (await context.ResaleListings.FindAsync(listing.Id))!.ListingStatus);
        Assert.False((await context.EscrowTransactions.FindAsync(escrow.Id))!.InSettlementBuffer);
    }

    [Fact]
    public async Task Handle_WhenHoldExpiredButListingStillTransacting_ShouldReleaseListingToVerified()
    {
        var (context, listing, escrow) = CreateTestFixture();
        listing.ListingStatus = ListingStatus.Transacting;
        escrow.UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await context.SaveChangesAsync();
        var handler = new ProcessSePayWebhookCommandHandler(context);

        var result = await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10008,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D",
            ReferenceCode = "FTLATE002"
        }), CancellationToken.None);

        Assert.Equal("RefundQueued", result.Data.EscrowStatus);
        Assert.Equal(ListingStatus.Verified, (await context.ResaleListings.FindAsync(listing.Id))!.ListingStatus);
    }

    [Fact]
    public async Task Handle_DuplicateLatePaymentWebhook_ShouldReturnIdempotentSuccess()
    {
        var (context, _, escrow) = CreateTestFixture();
        escrow.Status = EscrowStatus.RefundQueued;
        escrow.BankTransactionReference = "FTLATE001";
        await context.SaveChangesAsync();
        var handler = new ProcessSePayWebhookCommandHandler(context);

        var result = await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10009,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D",
            ReferenceCode = "FTLATE001"
        }), CancellationToken.None);

        Assert.True(result.Data.IsIdempotentDuplicate);
        Assert.Equal("RefundQueued", result.Data.EscrowStatus);
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
        var expected = EscrowTransaction.ComputeSettlementUnlockAt(
            before,
            TimeSpan.FromSeconds(86400));
        Assert.True(result.Success);
        Assert.Equal(EscrowStatus.Locked, dbEscrow!.Status);
        Assert.True(dbEscrow.InSettlementBuffer);
        Assert.NotEqual(holdExpiry, dbEscrow.UnlockAt);
        Assert.True(dbEscrow.UnlockAt >= expected.AddSeconds(-2));
        Assert.True(dbEscrow.UnlockAt <= expected.AddSeconds(2));
        Assert.Equal(ListingStatus.Sold, (await context.ResaleListings.FindAsync(listing.Id))!.ListingStatus);
    }

    [Fact]
    public void ComputeSettlementUnlockAt_CalculatesBufferFromUtcNow()
    {
        var buffer = TimeSpan.FromHours(24);
        var now = new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);
        var actual = EscrowTransaction.ComputeSettlementUnlockAt(now, buffer);
        Assert.Equal(now.AddHours(24), actual);
    }

    [Fact]
    public async Task Handle_ValidPaymentWebhook_ShouldEmailBuyerAndSeller()
    {
        var (context, _, _) = CreateTestFixture();
        var email = new Mock<IEmailService>();
        var templates = new Mock<IEmailTemplateService>();
        templates.Setup(t => t.GetBuyerTicketIssuedEmailHtml(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<decimal>())).Returns("<p>buyer</p>");
        templates.Setup(t => t.GetSellerEscrowLockedEmailHtml(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<string>())).Returns("<p>seller</p>");
        var handler = new ProcessSePayWebhookCommandHandler(context, templates.Object, email.Object);

        await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10001,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D thanh toan ve TicketShield",
            ReferenceCode = "FTMAIL001"
        }), CancellationToken.None);

        email.Verify(e => e.SendEmailAsync(
            "buyer@test.com",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        email.Verify(e => e.SendEmailAsync(
            "seller@test.com",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateWebhook_ShouldNotSendEmail()
    {
        var (context, _, escrow) = CreateTestFixture();
        escrow.Status = EscrowStatus.Locked;
        escrow.BankTransactionReference = "FTEXISTING123";
        await context.SaveChangesAsync();
        var email = new Mock<IEmailService>();
        var handler = new ProcessSePayWebhookCommandHandler(context, null, email.Object);

        await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10003,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D",
            ReferenceCode = "FTEXISTING123"
        }), CancellationToken.None);

        email.Verify(e => e.SendEmailAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenHoldExpired_ShouldEmailBuyerRefundNotice()
    {
        var (context, listing, escrow) = CreateTestFixture();
        listing.ListingStatus = ListingStatus.Verified;
        escrow.UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        var email = new Mock<IEmailService>();
        var handler = new ProcessSePayWebhookCommandHandler(context, null, email.Object);

        await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10007,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D thanh toan tre",
            ReferenceCode = "FTLATEMAIL"
        }), CancellationToken.None);

        email.Verify(e => e.SendEmailAsync(
            "buyer@test.com",
            It.Is<string>(s => s.Contains("refund", StringComparison.OrdinalIgnoreCase) || s.Contains("Late payment", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        email.Verify(e => e.SendEmailAsync(
            "seller@test.com",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidPayment_WhenVerificationServiceProvided_ShouldCallTransferOwnershipAndStoreNewTicketCodeAndQr()
    {
        var (context, listing, escrow) = CreateTestFixture();
        escrow.RecipientEmail = "buyer_recipient@test.com";
        escrow.RecipientName = "Buyer Recipient";
        await context.SaveChangesAsync();

        var templates = new Mock<IEmailTemplateService>();
        var email = new Mock<IEmailService>();
        var verification = new Mock<ITicketVerificationService>();

        verification.Setup(v => v.TransferOwnershipByListingId(
            listing.Id,
            escrow.BuyerId,
            "buyer_recipient@test.com",
            "Buyer Recipient",
            null,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketShield.Contracts.Organizer.V1.TransferOwnershipResponse
            {
                Outcome = TicketShield.Contracts.Organizer.V1.TransferOutcome.Transferred,
                NewTicket = new TicketShield.Contracts.Organizer.V1.TicketSnapshot
                {
                    Ticket = new TicketShield.Contracts.Organizer.V1.TicketReference
                    {
                        TicketCode = "BTC-NEW-PASS-9999"
                    }
                }
            });

        string? capturedTicketCode = null;
        string? capturedQr = null;

        templates.Setup(t => t.GetBuyerTicketIssuedEmailHtml(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<decimal>()))
            .Callback<string, string, string, string, string, string, string, string, string, decimal>(
                (bName, evName, evDate, venue, tier, seat, tCode, qr, escCode, amt) =>
                {
                    capturedTicketCode = tCode;
                    capturedQr = qr;
                })
            .Returns("<p>buyer email</p>");

        var handler = new ProcessSePayWebhookCommandHandler(context, templates.Object, email.Object, null, verification.Object);

        var result = await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 99991,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D test btc grpc transfer",
            ReferenceCode = "FTGRPC001"
        }), CancellationToken.None);

        Assert.True(result.Success);

        var updatedEscrow = await context.EscrowTransactions.FirstAsync(e => e.Id == escrow.Id);
        Assert.Equal("BTC-NEW-PASS-9999", updatedEscrow.NewTicketCode);
        Assert.Equal("BTC-NEW-PASS-9999", updatedEscrow.QrCodeData);
        Assert.Equal("BTC-NEW-PASS-9999", capturedTicketCode);
        Assert.Contains("BTC-NEW-PASS-9999", capturedQr);

        verification.Verify(v => v.TransferOwnershipByListingId(
            listing.Id,
            escrow.BuyerId,
            "buyer_recipient@test.com",
            "Buyer Recipient",
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidPayment_ShouldPersistLockedSold()
    {
        var (context, listing, escrow) = CreateTestFixture();
        await context.SaveChangesAsync();

        var verification = new Mock<ITicketVerificationService>();
        verification.Setup(v => v.TransferOwnershipByListingId(
            listing.Id,
            escrow.BuyerId,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketShield.Contracts.Organizer.V1.TransferOwnershipResponse
            {
                Outcome = TicketShield.Contracts.Organizer.V1.TransferOutcome.Transferred,
                NewTicket = new TicketShield.Contracts.Organizer.V1.TicketSnapshot
                {
                    Ticket = new TicketShield.Contracts.Organizer.V1.TicketReference
                    {
                        TicketCode = "BTC-NEW-PASS-TRACKER"
                    }
                }
            });

        var handler = new ProcessSePayWebhookCommandHandler(context, null, null, null, verification.Object);

        var result = await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 99992,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D tracker clear",
            ReferenceCode = "FTCLEAR001"
        }), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nameof(EscrowStatus.Locked), result.Data!.EscrowStatus);
        Assert.Equal(nameof(ListingStatus.Sold), result.Data.ListingStatus);

        context.ChangeTracker.Clear();
        var persisted = await context.EscrowTransactions
            .Include(e => e.Listing)
            .FirstAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Locked, persisted.Status);
        Assert.Equal(ListingStatus.Sold, persisted.Listing.ListingStatus);
        Assert.Equal("FTCLEAR001", persisted.BankTransactionReference);
    }
    [Fact]
    public async Task Handle_ValidPaymentWebhook_WithBundle_ShouldLockEscrowAndMarkAllListingsSold()
    {
        // Arrange
        var (context, anchorListing, escrow) = CreateTestFixture("TSBUNDLE01", 1100_000m);
        var bundleId = Guid.NewGuid();
        anchorListing.BundleId = bundleId;
        escrow.BundleId = bundleId;
        
        var childTicket = new Ticket
        {
            Id = Guid.NewGuid(),
            TicketCode = "TCKCHILD",
            EventId = anchorListing.EventId,
            TierId = anchorListing.TierId,
            CurrentOwnerId = anchorListing.SellerId,
            OriginalPrice = 500_000m,
            Status = TicketStatus.LockedForResale
        };
        context.Tickets.Add(childTicket);

        var childListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = anchorListing.EventId,
            TierId = anchorListing.TierId,
            SellerId = anchorListing.SellerId,
            TicketId = childTicket.Id,
            Ticket = childTicket,
            OriginalTicketCode = "TCKCHILD",
            OriginalPrice = 500_000m,
            ResalePrice = 500_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            IsBundleAllOrNothing = true
        };
        context.ResaleListings.Add(childListing);
        await context.SaveChangesAsync();

        var handler = new ProcessSePayWebhookCommandHandler(context);

        var request = new SePayWebhookRequest
        {
            Id = 10021,
            TransferType = "in",
            TransferAmount = 1100_000m,
            Content = "TSBUNDLE01 thanh toan mua combo",
            ReferenceCode = "FTBUNDLE01"
        };

        // Act
        var result = await handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        
        var dbEscrow = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.Equal(EscrowStatus.Locked, dbEscrow!.Status);
        Assert.Contains("TCK12345", dbEscrow.NewTicketCode);
        Assert.Contains("TCKCHILD", dbEscrow.NewTicketCode);
        
        var dbAnchor = await context.ResaleListings.FindAsync(anchorListing.Id);
        var dbChild = await context.ResaleListings.FindAsync(childListing.Id);
        Assert.Equal(ListingStatus.Sold, dbAnchor!.ListingStatus);
        Assert.Equal(ListingStatus.Sold, dbChild!.ListingStatus);
    }

    [Fact]
    public async Task Handle_LatePaymentWebhook_WithBundle_ShouldQueueRefundAndMarkAllListingsVerified()
    {
        // Arrange
        var (context, anchorListing, escrow) = CreateTestFixture("TSBUNDLE02", 1100_000m);
        var bundleId = Guid.NewGuid();
        anchorListing.BundleId = bundleId;
        escrow.BundleId = bundleId;
        escrow.UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-5); // Expired

        var childTicket2 = new Ticket
        {
            Id = Guid.NewGuid(),
            TicketCode = "TCKCHILD2",
            EventId = anchorListing.EventId,
            TierId = anchorListing.TierId,
            CurrentOwnerId = anchorListing.SellerId,
            OriginalPrice = 500_000m,
            Status = TicketStatus.LockedForResale
        };
        context.Tickets.Add(childTicket2);

        var childListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = anchorListing.EventId,
            TierId = anchorListing.TierId,
            SellerId = anchorListing.SellerId,
            TicketId = childTicket2.Id,
            Ticket = childTicket2,
            OriginalTicketCode = "TCKCHILD2",
            OriginalPrice = 500_000m,
            ResalePrice = 500_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            IsBundleAllOrNothing = true
        };
        context.ResaleListings.Add(childListing);
        await context.SaveChangesAsync();

        var handler = new ProcessSePayWebhookCommandHandler(context);

        var request = new SePayWebhookRequest
        {
            Id = 10022,
            TransferType = "in",
            TransferAmount = 1100_000m,
            Content = "TSBUNDLE02 thanh toan tre",
            ReferenceCode = "FTBUNDLE02"
        };

        // Act
        var result = await handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("RefundQueued", result.Data!.EscrowStatus);
        
        var dbEscrow = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.Equal(EscrowStatus.RefundQueued, dbEscrow!.Status);
        
        var dbAnchor = await context.ResaleListings.FindAsync(anchorListing.Id);
        var dbChild = await context.ResaleListings.FindAsync(childListing.Id);
        Assert.Equal(ListingStatus.Verified, dbAnchor!.ListingStatus);
        Assert.Equal(ListingStatus.Verified, dbChild!.ListingStatus);
    }

    [Fact]
    public async Task Handle_BundleTransferPartialFailure_ShouldLeaveChangeTrackerCleanAndRouteToTransferNeedsReview()
    {
        // Arrange
        var (context, anchorListing, escrow) = CreateTestFixture("TSBUNDLE03", 1100_000m);
        var bundleId = Guid.NewGuid();
        anchorListing.BundleId = bundleId;
        escrow.BundleId = bundleId;

        var childTicket3 = new Ticket
        {
            Id = Guid.NewGuid(),
            TicketCode = "TCKCHILD3",
            EventId = anchorListing.EventId,
            TierId = anchorListing.TierId,
            CurrentOwnerId = anchorListing.SellerId,
            OriginalPrice = 500_000m,
            Status = TicketStatus.LockedForResale
        };
        context.Tickets.Add(childTicket3);

        var childListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = anchorListing.EventId,
            TierId = anchorListing.TierId,
            SellerId = anchorListing.SellerId,
            TicketId = childTicket3.Id,
            Ticket = childTicket3,
            OriginalTicketCode = "TCKCHILD3",
            OriginalPrice = 500_000m,
            ResalePrice = 500_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            IsBundleAllOrNothing = true
        };
        context.ResaleListings.Add(childListing);
        await context.SaveChangesAsync();

        var verification = new Mock<ITicketVerificationService>();
        // Success on anchor ticket transfer (ticket 1), fail on child ticket transfer (ticket 2)
        verification.Setup(v => v.TransferOwnershipByListingId(anchorListing.Id, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketShield.Contracts.Organizer.V1.TransferOwnershipResponse
            {
                Outcome = TicketShield.Contracts.Organizer.V1.TransferOutcome.Transferred,
                NewTicket = new TicketShield.Contracts.Organizer.V1.TicketSnapshot { Ticket = new TicketShield.Contracts.Organizer.V1.TicketReference { TicketCode = "NEW1" } }
            });
        verification.Setup(v => v.TransferOwnershipByListingId(childListing.Id, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Internal, "Mock Organizer Error")));

        var handler = new ProcessSePayWebhookCommandHandler(context, null, null, null, verification.Object, null);

        var request = new SePayWebhookRequest
        {
            Id = 10023,
            TransferType = "in",
            TransferAmount = 1100_000m,
            Content = "TSBUNDLE03 thanh toan combo",
            ReferenceCode = "FTBUNDLE03"
        };

        // Act
        var result = await handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(nameof(EscrowStatus.TransferNeedsReview), result.Data!.EscrowStatus);

        var dbEscrow = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.Equal(EscrowStatus.TransferNeedsReview, dbEscrow!.Status);
        Assert.Equal("NEW1", dbEscrow.NewTicketCode);

        // Crucial 2-Phase Saga guarantee: ChangeTracker was clean during Phase 1,
        // so NO orphan buyer ticket was persisted to DB despite ticket 1 succeeding at Organizer.
        var persistedBuyerTickets = await context.Tickets.Where(t => t.TicketCode == "NEW1").ToListAsync();
        Assert.Empty(persistedBuyerTickets);

        // Neither listing should be erroneously rolled back to Verified
        var dbAnchor = await context.ResaleListings.FindAsync(anchorListing.Id);
        var dbChild = await context.ResaleListings.FindAsync(childListing.Id);
        Assert.NotEqual(ListingStatus.Verified, dbAnchor!.ListingStatus);
        Assert.NotEqual(ListingStatus.Verified, dbChild!.ListingStatus);
    }

    [Fact]
    public async Task Handle_BundleTransferImmediateFailure_WhenFirstTicketFails_RollsBackAllListingsAndQueuesRefund()
    {
        // Arrange
        var (context, anchorListing, escrow) = CreateTestFixture("TSBUNDLE04", 1100_000m);
        var bundleId = Guid.NewGuid();
        anchorListing.BundleId = bundleId;
        escrow.BundleId = bundleId;

        var childTicket4 = new Ticket
        {
            Id = Guid.NewGuid(),
            TicketCode = "TCKCHILD4",
            EventId = anchorListing.EventId,
            TierId = anchorListing.TierId,
            CurrentOwnerId = anchorListing.SellerId,
            OriginalPrice = 500_000m,
            Status = TicketStatus.LockedForResale
        };
        context.Tickets.Add(childTicket4);

        var childListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = anchorListing.EventId,
            TierId = anchorListing.TierId,
            SellerId = anchorListing.SellerId,
            TicketId = childTicket4.Id,
            Ticket = childTicket4,
            OriginalTicketCode = "TCKCHILD4",
            OriginalPrice = 500_000m,
            ResalePrice = 500_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            IsBundleAllOrNothing = true
        };
        context.ResaleListings.Add(childListing);
        await context.SaveChangesAsync();

        var verification = new Mock<ITicketVerificationService>();
        // Ticket 1 fails immediately with Unspecified (0 tickets transferred at Organizer)
        verification.Setup(v => v.TransferOwnershipByListingId(anchorListing.Id, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketShield.Contracts.Organizer.V1.TransferOwnershipResponse
            {
                Outcome = TicketShield.Contracts.Organizer.V1.TransferOutcome.Unspecified
            });

        var handler = new ProcessSePayWebhookCommandHandler(context, null, null, null, verification.Object, null);

        var request = new SePayWebhookRequest
        {
            Id = 10024,
            TransferType = "in",
            TransferAmount = 1100_000m,
            Content = "TSBUNDLE04 thanh toan combo fail ngay tu dau",
            ReferenceCode = "FTBUNDLE04"
        };

        // Act
        var result = await handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), result.Data!.EscrowStatus);

        var dbEscrow = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.Equal(EscrowStatus.RefundQueued, dbEscrow!.Status);

        // Since 0 tickets were transferred at Organizer, safe rollback to Verified is permitted
        var dbAnchor = await context.ResaleListings.FindAsync(anchorListing.Id);
        var dbChild = await context.ResaleListings.FindAsync(childListing.Id);
        Assert.Equal(ListingStatus.Verified, dbAnchor!.ListingStatus);
        Assert.Equal(ListingStatus.Verified, dbChild!.ListingStatus);
    }

    [Fact]
    public async Task Handle_TransferAmbiguousTimeout_WhenDeadlineExceeded_RoutesToTransferNeedsReviewWithoutAutoRefund()
    {
        // Arrange
        var (context, listing, escrow) = CreateTestFixture("TSTIMEOUT1", 550_000m);
        var verification = new Mock<ITicketVerificationService>();
        // gRPC network timeout (DeadlineExceeded)
        verification.Setup(v => v.TransferOwnershipByListingId(
                listing.Id,
                escrow.BuyerId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.DeadlineExceeded, "gRPC Deadline Exceeded")));

        var handler = new ProcessSePayWebhookCommandHandler(context, null, null, null, verification.Object);

        var request = new SePayWebhookRequest
        {
            Id = 10025,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TSTIMEOUT1 timeout mang",
            ReferenceCode = "FTTIMEOUT01"
        };

        // Act
        var result = await handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None);

        // Assert: Network timeout is ambiguous -> must route to TransferNeedsReview (NEVER auto-refund)
        Assert.True(result.Success);
        Assert.Equal(nameof(EscrowStatus.TransferNeedsReview), result.Data!.EscrowStatus);

        var dbEscrow = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.Equal(EscrowStatus.TransferNeedsReview, dbEscrow!.Status);

        var dbListing = await context.ResaleListings.FindAsync(listing.Id);
        Assert.NotEqual(ListingStatus.Verified, dbListing!.ListingStatus);
    }

    [Fact]
    public async Task Handle_WhenSaleCompletesNearEventStart_StoresTransferTimeApartFromUnlockTime()
    {
        var (context, listing, escrow) = CreateTestFixture();
        var eventStart = DateTimeOffset.UtcNow.AddHours(1);
        var eventEnd = eventStart.AddHours(3);
        listing.Event.EventStartAt = eventStart;
        listing.Event.EventEndAt = eventEnd;
        await context.SaveChangesAsync();

        var before = DateTimeOffset.UtcNow;
        var handler = new ProcessSePayWebhookCommandHandler(context);
        var result = await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10030,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D sang ten",
            ReferenceCode = "FTTRANSFER1"
        }), CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        Assert.True(result.Success);
        var stored = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.NotNull(stored!.TransferredAt);
        Assert.InRange(stored.TransferredAt.Value, before, after);
        Assert.InRange(stored.UnlockAt!.Value, before.AddSeconds(86400), after.AddSeconds(86400));
        Assert.NotEqual(stored.UnlockAt, stored.TransferredAt);
    }

    [Fact]
    public async Task Handle_WhenOwnershipTransferFails_LeavesTransferTimeEmpty()
    {
        var (context, listing, escrow) = CreateTestFixture();
        var verification = new Mock<ITicketVerificationService>();
        verification.Setup(v => v.TransferOwnershipByListingId(
                listing.Id,
                escrow.BuyerId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketShield.Contracts.Organizer.V1.TransferOwnershipResponse
            {
                Outcome = TicketShield.Contracts.Organizer.V1.TransferOutcome.Unspecified
            });

        var handler = new ProcessSePayWebhookCommandHandler(context, null, null, null, verification.Object);
        var result = await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10031,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D sang ten loi",
            ReferenceCode = "FTTRANSFER2"
        }), CancellationToken.None);

        Assert.Equal(nameof(EscrowStatus.RefundQueued), result.Data!.EscrowStatus);
        var stored = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.Null(stored!.TransferredAt);
    }

    [Fact]
    public async Task Handle_ValidPaymentWebhook_UsesConfiguredBufferSeconds()
    {
        var (context, listing, escrow) = CreateTestFixture();
        var settings = new Mock<IEscrowBufferSettings>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscrowBufferConfig { BufferSeconds = 30, CutoffSeconds = 7200 });

        var before = DateTimeOffset.UtcNow;
        var handler = new ProcessSePayWebhookCommandHandler(context, bufferSettings: settings.Object);
        var result = await handler.Handle(new ProcessSePayWebhookCommand(new SePayWebhookRequest
        {
            Id = 10032,
            TransferType = "in",
            TransferAmount = 550_000m,
            Content = "TS1A2B3C4D demo buffer",
            ReferenceCode = "FTBUFFER30"
        }), CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        Assert.True(result.Success);
        var stored = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.InRange(stored!.UnlockAt!.Value, before.AddSeconds(28), after.AddSeconds(32));
    }

    [Fact]
    public async Task Handle_WhenBuyerPaidForTwoOfThreeTicketsInBundle_MarksTwoSoldAndRevertsThirdToVerified()
    {
        // Arrange: 3-ticket flexible bundle where buyer held 2 tickets (listing1 + listing2)
        var (context, listing1, escrow) = CreateTestFixture("TSFLEX0001", 1_100_000m);
        var bundleId = Guid.NewGuid();
        listing1.BundleId = bundleId;
        listing1.BundleTotalTickets = 3;
        listing1.IsBundleAllOrNothing = false;
        escrow.BundleId = bundleId;

        var ticket2 = new Ticket
        {
            Id = Guid.NewGuid(),
            TicketCode = "TCK-BND-002",
            EventId = listing1.EventId,
            TierId = listing1.TierId,
            CurrentOwnerId = listing1.SellerId,
            OriginalPrice = 500_000m,
            Status = TicketStatus.LockedForResale
        };
        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = listing1.EventId,
            TierId = listing1.TierId,
            SellerId = listing1.SellerId,
            TicketId = ticket2.Id,
            Ticket = ticket2,
            OriginalTicketCode = "TCK-BND-002",
            OriginalPrice = 500_000m,
            ResalePrice = 500_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            BundleTotalTickets = 3,
            IsBundleAllOrNothing = false
        };

        var ticket3 = new Ticket
        {
            Id = Guid.NewGuid(),
            TicketCode = "TCK-BND-003",
            EventId = listing1.EventId,
            TierId = listing1.TierId,
            CurrentOwnerId = listing1.SellerId,
            OriginalPrice = 500_000m,
            Status = TicketStatus.LockedForResale
        };
        var listing3 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = listing1.EventId,
            TierId = listing1.TierId,
            SellerId = listing1.SellerId,
            TicketId = ticket3.Id,
            Ticket = ticket3,
            OriginalTicketCode = "TCK-BND-003",
            OriginalPrice = 500_000m,
            ResalePrice = 500_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            BundleTotalTickets = 3,
            IsBundleAllOrNothing = false
        };

        context.Tickets.AddRange(ticket2, ticket3);
        context.ResaleListings.AddRange(listing2, listing3);

        // Buyer selected listing1 and listing2
        context.EscrowTransactionListings.AddRange(
            new EscrowTransactionListing { EscrowId = escrow.Id, ListingId = listing1.Id },
            new EscrowTransactionListing { EscrowId = escrow.Id, ListingId = listing2.Id }
        );
        await context.SaveChangesAsync();

        var handler = new ProcessSePayWebhookCommandHandler(context);
        var request = new SePayWebhookRequest
        {
            Id = 10099,
            TransferType = "in",
            TransferAmount = 1_100_000m,
            Content = "TSFLEX0001 thanh toan mua 2 ve",
            ReferenceCode = "FTFLEX01"
        };

        // Act
        var result = await handler.Handle(new ProcessSePayWebhookCommand(request), CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        var dbEscrow = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.Equal(EscrowStatus.Locked, dbEscrow!.Status);

        // The 2 purchased tickets must be Sold and new tickets created for buyer
        var dbListing1 = await context.ResaleListings.FindAsync(listing1.Id);
        var dbListing2 = await context.ResaleListings.FindAsync(listing2.Id);
        Assert.Equal(ListingStatus.Sold, dbListing1!.ListingStatus);
        Assert.Equal(ListingStatus.Sold, dbListing2!.ListingStatus);

        var buyerTickets = await context.Tickets.Where(t => t.CurrentOwnerId == escrow.BuyerId).ToListAsync();
        Assert.Equal(2, buyerTickets.Count);
        Assert.All(buyerTickets, t => Assert.Equal(TicketStatus.Valid, t.Status));

        // The 3rd ticket (unpurchased) must revert to Verified and remain with seller!
        var dbListing3 = await context.ResaleListings.FindAsync(listing3.Id);
        var dbTicket3 = await context.Tickets.FindAsync(ticket3.Id);
        Assert.Equal(ListingStatus.Verified, dbListing3!.ListingStatus);
        Assert.Equal(listing1.SellerId, dbTicket3!.CurrentOwnerId);
    }
}
