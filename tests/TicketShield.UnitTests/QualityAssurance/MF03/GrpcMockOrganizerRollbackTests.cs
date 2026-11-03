using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// TEST-3.6.3_Rollback test on gRPC MockOrganizer failure
/// Description: Giả lập MockOrganizer trả lỗi khi sang tên, xác minh Listing và Escrow được Rollback an toàn 100%.
/// Bao gồm kiểm thử tất cả các mã lỗi gRPC (Unavailable, DeadlineExceeded, Internal, FailedPrecondition, Aborted),
/// lỗi mạng SocketException, lỗi Unspecified outcome (FIX Lỗi 20 - tuyệt đối không fallback mã vé cũ của Seller),
/// gói vé combo partial rollback, bảo vệ bồi hoàn 100% tiền Buyer, và cô lập thông báo/email.
/// </summary>
public class GrpcMockOrganizerRollbackTests
{
    private static (TicketShieldDbContext db, ResaleListing listing, EscrowTransaction escrow, ShadowUser seller, ShadowUser buyer) CreateFixture(
        string paymentReference = "TSROLL0001",
        decimal totalBuyerPaid = 1_050_000m,
        DateTimeOffset? eventStartAt = null)
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new TicketShieldDbContext(options);

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "MockOrganizer Grand BTC",
            OfficialEmail = "organizer@mockorganizer.test"
        };

        var startAt = eventStartAt ?? DateTimeOffset.UtcNow.AddDays(10);

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "National Rock Symphony 2026",
            Venue = "My Dinh National Stadium",
            EventStartAt = startAt,
            EventEndAt = startAt.AddHours(4),
            ResaleDeadline = startAt.AddHours(-2),
            Organizer = organizer
        };

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierName = "VIP Standing",
            OriginalPrice = 1_000_000m,
            Event = ev
        };

        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller@rollback.test",
            FullName = "Nguyen Van Seller"
        };

        var buyer = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer@rollback.test",
            FullName = "Tran Van Buyer"
        };

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-MOCK-001",
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
            RecipientName = "Tran Van Buyer",
            RecipientEmail = "buyer@rollback.test"
        };

        listing.EscrowTransaction = escrow;

        db.Organizers.Add(organizer);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(seller, buyer);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        db.SaveChanges();

        return (db, listing, escrow, seller, buyer);
    }

    private static SePayWebhookRequest CreatePayload(string paymentReference, decimal amount = 1_050_000m, string bankRef = "FT_MOCK_FAIL_001")
    {
        return new SePayWebhookRequest
        {
            Id = 88001,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentReference} ROLLBACK TEST",
            TransferType = "in",
            TransferAmount = amount,
            ReferenceCode = bankRef
        };
    }

    #region 1. gRPC StatusCode Failures (Unavailable, DeadlineExceeded, Internal, FailedPrecondition, Aborted)

    [Fact]
    public async Task MockOrganizer_ThrowsUnavailable_RollsBackListingToVerified_AndEscrowToRefundQueued_100PercentSafe()
    {
        // Arrange: Giả lập MockOrganizer trả lỗi Unavailable (Server gRPC sập hoặc đang restart)
        const string paymentRef = "TSUNAVAIL1"; // Đúng định dạng 10 ký tự: TS + 8 ký tự
        const string bankRef = "FT_GRPC_UNAVAIL";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "MockOrganizer gRPC service is offline or restarting")));

        var handler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object);

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(CreatePayload(paymentRef, bankRef: bankRef)), CancellationToken.None);

        // Assert Response
        Assert.True(response.Success);
        Assert.False(response.Data!.IsIdempotentDuplicate);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), response.Data.ListingStatus);
        Assert.Contains("Saga Compensation", response.Message);

        // Assert Database Rollback 100% An toàn:
        db.ChangeTracker.Clear();
        var rolledBackListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var rolledBackEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        // 1. Listing được trả về Verified an toàn (không bị kẹt ở Sold hay Transacting)
        Assert.Equal(ListingStatus.Verified, rolledBackListing.ListingStatus);
        Assert.Equal("TCK-ORIG-MOCK-001", rolledBackListing.OriginalTicketCode); // Vé gốc của seller không bị mất

        // 2. Escrow được đưa vào hàng đợi hoàn tiền 100% cho buyer
        Assert.Equal(EscrowStatus.RefundQueued, rolledBackEscrow.Status);
        Assert.False(rolledBackEscrow.InSettlementBuffer); // Tiền không bị treo để giải ngân cho seller
        Assert.Equal(bankRef, rolledBackEscrow.BankTransactionReference);
        Assert.Null(rolledBackEscrow.NewTicketCode); // TUYỆT ĐỐI không có vé mới mồ côi
        Assert.Null(rolledBackEscrow.TransferredAt);
    }

    [Fact]
    public async Task MockOrganizer_ThrowsDeadlineExceededTimeout_RollsBackListingToVerified_AndEscrowToRefundQueued_100PercentSafe()
    {
        // Arrange: Giả lập MockOrganizer bị quá hạn phản hồi (Timeout 5000ms / DeadlineExceeded)
        const string paymentRef = "TSTIMEOUT1";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.DeadlineExceeded, "MockOrganizer did not respond within 5000ms deadline")));

        var handler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object);

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(CreatePayload(paymentRef)), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), response.Data.ListingStatus);

        db.ChangeTracker.Clear();
        var rolledBackListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var rolledBackEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, rolledBackListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, rolledBackEscrow.Status);
        Assert.False(rolledBackEscrow.InSettlementBuffer);
        Assert.Null(rolledBackEscrow.NewTicketCode);
    }

    [Fact]
    public async Task MockOrganizer_ThrowsInternalError_RollsBackListingToVerified_AndEscrowToRefundQueued_100PercentSafe()
    {
        // Arrange: Giả lập MockOrganizer gặp lỗi nội bộ (Crash database bên BTC / StatusCode.Internal)
        const string paymentRef = "TSINTERNL1";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Internal, "MockOrganizer PostgreSQL database connection terminated")));

        var handler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object);

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(CreatePayload(paymentRef)), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), response.Data.ListingStatus);

        db.ChangeTracker.Clear();
        var rolledBackListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var rolledBackEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, rolledBackListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, rolledBackEscrow.Status);
        Assert.Null(rolledBackEscrow.NewTicketCode);
    }

    [Fact]
    public async Task MockOrganizer_ThrowsFailedPrecondition_RollsBackListingToVerified_AndEscrowToRefundQueued_100PercentSafe()
    {
        // Arrange: Giả lập MockOrganizer từ chối: vé đã bị dùng hoặc hết hiệu lực (TICKET_NOT_AVAILABLE / StatusCode.FailedPrecondition)
        const string paymentRef = "TSFAILPC01";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.FailedPrecondition, "TICKET_ALREADY_USED_OR_REVOKED")));

        var handler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object);

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(CreatePayload(paymentRef)), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), response.Data.ListingStatus);

        db.ChangeTracker.Clear();
        var rolledBackListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var rolledBackEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, rolledBackListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, rolledBackEscrow.Status);
        Assert.Null(rolledBackEscrow.NewTicketCode);
    }

    [Fact]
    public async Task MockOrganizer_ThrowsAbortedLockGenerationConflict_RollsBackListingToVerified_AndEscrowToRefundQueued()
    {
        // Arrange: Giả lập MockOrganizer báo lỗi xung đột phiên lock (LOCK_GENERATION_CONFLICT / StatusCode.Aborted)
        const string paymentRef = "TSABORT001";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Aborted, "LOCK_GENERATION_CONFLICT")));

        var handler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object);

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(CreatePayload(paymentRef)), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), response.Data.ListingStatus);

        db.ChangeTracker.Clear();
        var rolledBackListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var rolledBackEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, rolledBackListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, rolledBackEscrow.Status);
        Assert.Null(rolledBackEscrow.NewTicketCode);
    }

    #endregion

    #region 2. TransferOutcome.Unspecified (FIX Lỗi 20 - Tuyệt đối không fallback sang mã vé cũ của Seller)

    [Fact]
    public async Task MockOrganizer_ReturnsUnspecifiedOutcome_RejectsOldTicketFallback_TriggersSagaCompensationRollback()
    {
        // Arrange: MockOrganizer trả về TransferOutcome.Unspecified (phiên sang tên không được xác nhận)
        const string paymentRef = "TSUNSPEC01";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Unspecified
            });

        var handler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object);

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(CreatePayload(paymentRef)), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), response.Data.ListingStatus);

        // Verification: Tuyệt đối không fallback sang "TCK-ORIG-MOCK-001"
        db.ChangeTracker.Clear();
        var rolledBackListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var rolledBackEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, rolledBackListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, rolledBackEscrow.Status);
        Assert.Null(rolledBackEscrow.NewTicketCode); // Không bao giờ lưu OriginalTicketCode làm NewTicketCode
    }

    [Fact]
    public async Task MockOrganizer_ReturnsNullNewTicketCode_RejectsProtocolViolation_RollsBackListingAndEscrowSafely()
    {
        // Arrange: MockOrganizer trả Transferred nhưng NewTicket bị null
        const string paymentRef = "TSNULLCOD1";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Transferred,
                NewTicket = null // Vi phạm hợp đồng trả vé
            });

        var handler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object);

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(CreatePayload(paymentRef)), CancellationToken.None);

        // Assert: Bắt được lỗi, kích hoạt Saga rollback an toàn
        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), response.Data.ListingStatus);

        db.ChangeTracker.Clear();
        var rolledBackEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.RefundQueued, rolledBackEscrow.Status);
        Assert.Null(rolledBackEscrow.NewTicketCode);
    }

    #endregion

    #region 3. Network Drop (SocketException)

    [Fact]
    public async Task MockOrganizer_ThrowsNetworkSocketException_RollsBackListingAndEscrowSafely()
    {
        // Arrange: Rớt mạng vật lý / ngắt kết nối socket giữa TicketShield và MockOrganizer
        const string paymentRef = "TSSOCKET01";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset));

        var handler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object);

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(CreatePayload(paymentRef)), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), response.Data.ListingStatus);

        db.ChangeTracker.Clear();
        var rolledBackListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var rolledBackEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, rolledBackListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, rolledBackEscrow.Status);
        Assert.Null(rolledBackEscrow.NewTicketCode);
    }

    #endregion

    #region 4. Multi-Ticket Bundle Partial Failure Rollback (Tính toàn vẹn gói Combo)

    [Fact]
    public async Task MockOrganizer_BundleTransfer_WhenSecondTicketFails_RollsBackAllBundleListingsAtomically_100PercentSafe()
    {
        // Arrange: Gói combo gồm 2 vé (Ticket 1 sang tên thành công, Ticket 2 bị lỗi MockOrganizer)
        const string paymentRef = "TSBNDFAIL1";
        var bundleId = Guid.NewGuid();
        var (db, listing1, escrow, seller, buyer) = CreateFixture(paymentReference: paymentRef, totalBuyerPaid: 2_100_000m);
        listing1.BundleId = bundleId;
        listing1.BundleTotalTickets = 2;
        escrow.BundleId = bundleId;

        var listing2 = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = listing1.EventId,
            TierId = listing1.TierId,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-MOCK-002",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting,
            BundleId = bundleId,
            BundleTotalTickets = 2,
            Event = listing1.Event,
            Tier = listing1.Tier
        };

        db.ResaleListings.Add(listing2);
        await db.SaveChangesAsync();

        var mockVerification = new Mock<ITicketVerificationService>();

        // Vé 1 sang tên thành công
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing1.Id, buyer.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Transferred,
                NewTicket = new TicketSnapshot { Ticket = new TicketReference { TicketCode = "NEW-TCK-BUNDLE-01" } }
            });

        // Vé 2 gặp lỗi sập MockOrganizer
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing2.Id, buyer.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "MockOrganizer crash on second ticket in bundle")));

        var handler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object);

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(CreatePayload(paymentRef, amount: 2_100_000m)), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);

        // Assert ACID ATOMICITY: CẢ HAI vé trong combo đều phải rollback về Verified
        // Không được phép để xảy ra tình trạng vé 1 đã Sold mà vé 2 lỗi!
        db.ChangeTracker.Clear();
        var allBundleListings = await db.ResaleListings.Where(l => l.BundleId == bundleId).ToListAsync();
        Assert.Equal(2, allBundleListings.Count);
        Assert.All(allBundleListings, l => Assert.Equal(ListingStatus.Verified, l.ListingStatus));

        var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.RefundQueued, updatedEscrow.Status);
        Assert.False(updatedEscrow.InSettlementBuffer);
        Assert.Null(updatedEscrow.NewTicketCode); // Không cấp bất kỳ vé nào trong combo khi có vé lỗi
    }

    #endregion

    #region 5. Event Resale Protection on Saga Rollback

    [Fact]
    public async Task MockOrganizer_FailureDuringTransfer_SafelyRollsBackListingToVerified_AndEscrowToRefundQueued()
    {
        // Arrange
        const string paymentRef = "TSPASTCUT1";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "MockOrganizer connection dropped")));

        var handler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object);

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(CreatePayload(paymentRef)), CancellationToken.None);

        // Assert: Escrow được đưa vào RefundQueued, Listing chuyển về Verified
        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);
        Assert.Equal(ListingStatus.Verified.ToString(), response.Data.ListingStatus);

        db.ChangeTracker.Clear();
        var rolledBackListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var rolledBackEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, rolledBackListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, rolledBackEscrow.Status);
        Assert.False(rolledBackEscrow.InSettlementBuffer);
        Assert.Null(rolledBackEscrow.NewTicketCode);
    }

    #endregion

    #region 6. Notification & Email Isolation (Không gửi thông báo duyệt thanh toán khi rollback)

    [Fact]
    public async Task MockOrganizer_Failure_PreventsPaymentApprovedNotificationsAndTicketEmails()
    {
        // Arrange
        const string paymentRef = "TSNONOTIF1";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "BTC gateway down")));

        var mockNotifier = new Mock<IPaymentRealtimeNotifier>();
        var mockEmailService = new Mock<IEmailService>();
        var mockEmailTemplates = new Mock<IEmailTemplateService>();

        var handler = new ProcessSePayWebhookCommandHandler(
            db,
            mockEmailTemplates.Object,
            mockEmailService.Object,
            NullLogger<ProcessSePayWebhookCommandHandler>.Instance,
            mockVerification.Object,
            mockNotifier.Object);

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(CreatePayload(paymentRef)), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), response.Data!.EscrowStatus);

        // Verify: TUYỆT ĐỐI KHÔNG gửi thông báo hay email xác nhận mua vé thành công
        mockNotifier.Verify(n => n.NotifyPaymentApprovedAsync(It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        mockNotifier.Verify(n => n.NotifyOrderSettledAsync(It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        mockEmailService.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region 7. Idempotency after Rollback (Webhook trùng lặp sau rollback không thử sang tên lại)

    [Fact]
    public async Task MockOrganizer_DuplicateWebhookAfterRollback_RecognizesRefundQueued_ReturnsIdempotentDuplicateWithoutRetryingTransfer()
    {
        // Arrange
        const string paymentRef = "TSRETRY001";
        const string bankRef = "FT_RETRY_9999";
        var (db, listing, escrow, _, _) = CreateFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "BTC gateway timeout")));

        var handler = new ProcessSePayWebhookCommandHandler(
            db, null, null, NullLogger<ProcessSePayWebhookCommandHandler>.Instance, mockVerification.Object);

        var payload = CreatePayload(paymentRef, bankRef: bankRef);

        // Act 1: Webhook lần 1 gặp lỗi MockOrganizer -> Rollback về RefundQueued
        var res1 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);
        Assert.True(res1.Success);
        Assert.False(res1.Data!.IsIdempotentDuplicate);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), res1.Data.EscrowStatus);

        // Act 2: Webhook lần 2 trùng mã giao dịch SePay gửi lại (retry)
        var res2 = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert 2: Nhận diện Escrow đã ở RefundQueued -> Idempotent Duplicate
        Assert.True(res2.Success);
        Assert.True(res2.Data!.IsIdempotentDuplicate);
        Assert.Equal(nameof(EscrowStatus.RefundQueued), res2.Data.EscrowStatus);
        Assert.Contains("Payment Idempotency", res2.Message);

        // gRPC transfer chỉ được thử gọi DUY NHẤT 1 LẦN trong lần 1, TUYỆT ĐỐI không gọi lại trong lần 2
        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Trạng thái DB được bảo toàn nguyên vẹn
        db.ChangeTracker.Clear();
        var currentListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);
        var currentEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);

        Assert.Equal(ListingStatus.Verified, currentListing.ListingStatus);
        Assert.Equal(EscrowStatus.RefundQueued, currentEscrow.Status);
        Assert.Null(currentEscrow.NewTicketCode);
    }

    #endregion
}
