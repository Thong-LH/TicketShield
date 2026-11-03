using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// US-3.6_MF03: QA Suite - Phục hồi lỗi gRPC & Saga Compensation (gRPC Error Recovery & Resilience)
/// Kiểm thử tự động toàn diện: Phục hồi khi gRPC rớt kết nối (Unavailable), quá hạn (DeadlineExceeded),
/// lỗi mạng, phiên sang tên bị BTC từ chối (Unspecified), và bồi hoàn giao dịch gói vé (Bundle Saga).
/// </summary>
public class GrpcErrorRecoveryQATests
{
    private static (TicketShieldDbContext db, ResaleListing listing, EscrowTransaction escrow) CreateFixture(
        string paymentReference = "TSGRPC0001",
        decimal totalBuyerPaid = 1_050_000m)
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new TicketShieldDbContext(options);

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "QA Organizer BTC",
            OfficialEmail = "organizer@qa.test"
        };

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "QA Rock Festival 2026",
            Venue = "My Dinh National Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(14),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(14).AddHours(4),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(13),
            Organizer = organizer
        };

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierName = "GA Zone",
            OriginalPrice = 1_000_000m,
            Event = ev
        };

        var seller = new ShadowUser { Id = Guid.NewGuid(), Email = "seller@qa.test", FullName = "Nguyen Seller" };
        var buyer = new ShadowUser { Id = Guid.NewGuid(), Email = "buyer@qa.test", FullName = "Tran Buyer" };

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-GRPC-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting,
            Event = ev,
            Tier = tier
        };

        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = 1_000_000m,
            BuyerFee = 50_000m,
            SellerFee = 30_000m,
            TotalBuyerPaid = totalBuyerPaid,
            NetSellerPayout = 970_000m,
            PaymentReference = paymentReference,
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10),
            RecipientName = "Tran Buyer",
            RecipientEmail = "buyer@qa.test"
        };

        listing.EscrowTransaction = escrow;

        db.Organizers.Add(organizer);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(seller, buyer);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        db.SaveChanges();

        return (db, listing, escrow);
    }

    [Fact]
    public async Task SePayWebhook_WhenGrpcThrowsRpcExceptionUnavailable_TriggersSagaCompensation()
    {
        // Arrange
        var (db, listing, escrow) = CreateFixture();

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "BTC Organizer gRPC Server is offline or restarting")));

        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 30001,
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "FT_GRPC_UNAVAIL"
        };

        // Act - Webhook handles the exception internally via Saga Compensation
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("RefundQueued", response.Data!.EscrowStatus);
        Assert.Equal("Verified", response.Data.ListingStatus);
        Assert.Contains("Saga Compensation", response.Message);

        // Verify DB Rollback:
        // 1. Listing must return to safe state (Verified), NOT Sold
        // 2. Escrow must be in RefundQueued with InSettlementBuffer = false
        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, updatedListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, updatedEscrow.Status);
        Assert.False(updatedEscrow.InSettlementBuffer);
        Assert.Equal("FT_GRPC_UNAVAIL", updatedEscrow.BankTransactionReference);
    }

    [Fact]
    public async Task SePayWebhook_WhenGrpcThrowsRpcExceptionDeadlineExceeded_TriggersSagaCompensation()
    {
        // Arrange
        var (db, listing, escrow) = CreateFixture();

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.DeadlineExceeded, "Timeout 5000ms exceeded while waiting for BTC response")));

        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 30002,
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "FT_GRPC_TIMEOUT"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("RefundQueued", response.Data!.EscrowStatus);
        Assert.Equal("Verified", response.Data.ListingStatus);

        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, updatedListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, updatedEscrow.Status);
    }

    [Fact]
    public async Task SePayWebhook_WhenGrpcThrowsNetworkSocketException_TriggersSagaCompensation()
    {
        // Arrange
        var (db, listing, escrow) = CreateFixture();

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset));

        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 30003,
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "FT_CONN_RESET"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("RefundQueued", response.Data!.EscrowStatus);
        Assert.Equal("Verified", response.Data.ListingStatus);

        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, updatedListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, updatedEscrow.Status);
    }

    [Fact]
    public async Task SePayWebhook_WhenOrganizerReturnsUnspecifiedOutcome_TriggersSagaCompensation()
    {
        // Arrange - BTC returns Unspecified outcome (e.g. Session not found / already invalidated)
        var (db, listing, escrow) = CreateFixture();

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Unspecified // BTC rejected
            });

        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 30004,
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "FT_UNSPECIFIED_OUTCOME"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("RefundQueued", response.Data!.EscrowStatus);
        Assert.Equal("Verified", response.Data.ListingStatus);

        // Buyer refund queued, ticket returned to Verified, NOT given old code
        db.ChangeTracker.Clear();
        var updatedListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, updatedListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, updatedEscrow.Status);
    }

    [Fact]
    public async Task SePayWebhook_BundleTransfer_WhenSecondTicketFails_RollsBackAllTicketsInBundle()
    {
        // Arrange - Bundle with 2 tickets
        var (db, listing1, escrow) = CreateFixture(totalBuyerPaid: 2_100_000m);
        var bundleId = Guid.NewGuid();
        listing1.BundleId = bundleId;
        escrow.BundleId = bundleId;

        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = listing1.EventId,
            TierId = listing1.TierId,
            SellerId = listing1.SellerId,
            OriginalTicketCode = "TCK-ORIG-GRPC-002",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            CreatedAt = listing1.CreatedAt.AddSeconds(1)
        };

        db.ResaleListings.Add(listing2);
        await db.SaveChangesAsync();

        var mockVerification = new Mock<ITicketVerificationService>();

        // Ticket 1 succeeds
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing1.Id, It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Transferred,
                NewTicket = new TicketSnapshot
                {
                    Ticket = new TicketReference { TicketCode = "TCK-NEW-001" }
                }
            });

        // Ticket 2 fails with gRPC exception
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing2.Id, It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "BTC gateway down for ticket 2")));

        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 30005,
            Content = $"THANH TOAN {escrow.PaymentReference}",
            TransferType = "in",
            TransferAmount = 2_100_000m,
            ReferenceCode = "FT_BUNDLE_PARTIAL_FAIL"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("RefundQueued", response.Data!.EscrowStatus);

        // SAGA COMPENSATION: Both listings must be rolled back to Verified, NONE left as Sold
        db.ChangeTracker.Clear();
        var allBundleListings = await db.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.Equal(2, allBundleListings.Count);
        Assert.All(allBundleListings, l => Assert.Equal(ListingStatus.Verified, l.ListingStatus));

        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.RefundQueued, updatedEscrow.Status);
        Assert.False(updatedEscrow.InSettlementBuffer);
    }
}
