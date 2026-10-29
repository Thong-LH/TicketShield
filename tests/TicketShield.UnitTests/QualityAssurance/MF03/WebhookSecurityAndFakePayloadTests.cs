using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TicketShield.API.Controllers;
using TicketShield.Application.Common.Configurations;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessTicketUsedWebhook;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// TEST-3.6.4_Webhook Security test with invalid signatures and fake payloads
/// Description: Kiểm thử bảo mật: từ chối các request webhook không hợp lệ hoặc thiếu chữ ký bảo mật.
/// Bao gồm kiểm thử thiếu header, secret token giả mạo, sai định dạng tiền tố, chữ hoa/thường (case mismatch),
/// payload giả mạo không có PaymentReference hợp lệ, mã thanh toán không tồn tại, tấn công thiếu tiền (underpayment),
/// rút tiền (money drain - transferType=out), số tiền âm, và payload webhook giả mạo gửi tới Ban tổ chức.
/// </summary>
public class WebhookSecurityAndFakePayloadTests
{
    private const string SePayValidSecret = "SePaySecret_SecurityTesting_Token_2026";
    private const string OrganizerValidSecret = "OrganizerSecret_SecurityTesting_Token_2026";

    #region Fixture Helpers

    private static (WebhooksController controller, Mock<ISender> mediatorMock) CreateSePayController()
    {
        var options = Options.Create(new VietQrSettings
        {
            WebhookSecret = SePayValidSecret
        });

        var mediatorMock = new Mock<ISender>();
        mediatorMock.Setup(m => m.Send(It.IsAny<ProcessSePayWebhookCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProcessSePayWebhookResponse>.SuccessResponse(
                new ProcessSePayWebhookResponse
                {
                    EscrowStatus = "Locked",
                    IsIdempotentDuplicate = false
                },
                "Webhook processed successfully"));

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(ISender))).Returns(mediatorMock.Object);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider.Object
        };

        var controller = new WebhooksController(options, NullLogger<WebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, mediatorMock);
    }

    private static (OrganizerWebhooksController controller, Mock<ISender> mediatorMock) CreateOrganizerController()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "OrganizerWebhook:Secret", OrganizerValidSecret }
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var mediatorMock = new Mock<ISender>();
        mediatorMock.Setup(m => m.Send(It.IsAny<ProcessTicketUsedWebhookCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProcessTicketUsedWebhookResponse>.SuccessResponse(
                new ProcessTicketUsedWebhookResponse
                {
                    EscrowId = Guid.NewGuid(),
                    EscrowStatus = "Releasing"
                },
                "Organizer webhook processed successfully"));

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(ISender))).Returns(mediatorMock.Object);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider.Object
        };

        var controller = new OrganizerWebhooksController(NullLogger<OrganizerWebhooksController>.Instance, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, mediatorMock);
    }

    private static (TicketShieldDbContext db, ResaleListing listing, EscrowTransaction escrow) CreateDatabaseFixture(
        string paymentReference = "TSSECURE01",
        decimal totalBuyerPaid = 1_050_000m,
        EscrowStatus escrowStatus = EscrowStatus.Pending,
        ListingStatus listingStatus = ListingStatus.Transacting)
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new TicketShieldDbContext(options);

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "Security Concert BTC",
            OfficialEmail = "security@concert.test"
        };

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "Cyber Security Arena 2026",
            Venue = "National Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(10),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(4),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(9),
            Organizer = organizer
        };

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierName = "Cyber Lounge",
            OriginalPrice = 1_000_000m,
            Event = ev
        };

        var seller = new ShadowUser { Id = Guid.NewGuid(), Email = "seller@sec.test", FullName = "Seller Security" };
        var buyer = new ShadowUser { Id = Guid.NewGuid(), Email = "buyer@sec.test", FullName = "Buyer Security" };

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-ORIG-SEC-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = listingStatus,
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
            Status = escrowStatus,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(10),
            RecipientName = "Buyer Security",
            RecipientEmail = "buyer@sec.test",
            NewTicketCode = listingStatus == ListingStatus.Sold ? "TCK-NEW-SEC-001" : null
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

    #endregion

    #region 1. Invalid Signatures & Missing Authentication Headers (Từ chối thiếu/sai chữ ký bảo mật)

    [Fact]
    public async Task SePayWebhook_MissingAuthorizationHeader_Returns401Unauthorized_AndNeverDispatchesMediator()
    {
        // Arrange
        var (controller, mediatorMock) = CreateSePayController();
        // Không truyền Authorization header
        var payload = new SePayWebhookRequest { Id = 1, TransferType = "in", TransferAmount = 100_000 };

        // Act
        var result = await controller.HandleSePayWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<object>>(unauthorizedResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("Thiếu header Authorization", apiResponse.Message);

        // Verify Mediator TUYỆT ĐỐI KHÔNG được gọi
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessSePayWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SePayWebhook_EmptyOrWhitespaceAuthorizationHeader_Returns401Unauthorized(string emptyAuth)
    {
        // Arrange
        var (controller, mediatorMock) = CreateSePayController();
        controller.HttpContext.Request.Headers["Authorization"] = emptyAuth;
        var payload = new SePayWebhookRequest { Id = 1, TransferType = "in", TransferAmount = 100_000 };

        // Act
        var result = await controller.HandleSePayWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessSePayWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SePayWebhook_FakeSecretToken_Returns401Unauthorized_AndNeverDispatchesMediator()
    {
        // Arrange: Chữ ký/secret giả mạo của kẻ tấn công
        var (controller, mediatorMock) = CreateSePayController();
        controller.HttpContext.Request.Headers["Authorization"] = "Bearer FakeHackerSecretToken_99999";
        var payload = new SePayWebhookRequest { Id = 1, TransferType = "in", TransferAmount = 100_000 };

        // Act
        var result = await controller.HandleSePayWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<object>>(unauthorizedResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("Secret Webhook không hợp lệ", apiResponse.Message);

        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessSePayWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SePayWebhook_SecretCaseMismatch_OrdinalComparisonRejects_Returns401Unauthorized()
    {
        // Arrange: Secret sai chữ hoa/thường (StringComparison.Ordinal bắt buộc phải khớp chính xác)
        var (controller, mediatorMock) = CreateSePayController();
        controller.HttpContext.Request.Headers["Authorization"] = "Bearer " + SePayValidSecret.ToLowerInvariant();
        var payload = new SePayWebhookRequest { Id = 1, TransferType = "in", TransferAmount = 100_000 };

        // Act
        var result = await controller.HandleSePayWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessSePayWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Token " + SePayValidSecret)]
    [InlineData("Basic " + SePayValidSecret)]
    [InlineData("Digest " + SePayValidSecret)]
    public async Task SePayWebhook_UnsupportedAuthPrefix_TokenOrBasicScheme_Returns401Unauthorized(string authHeader)
    {
        // Arrange: Prefix không hợp lệ (hệ thống chỉ chấp nhận Apikey, Bearer hoặc raw secret)
        var (controller, mediatorMock) = CreateSePayController();
        controller.HttpContext.Request.Headers["Authorization"] = authHeader;
        var payload = new SePayWebhookRequest { Id = 1, TransferType = "in", TransferAmount = 100_000 };

        // Act
        var result = await controller.HandleSePayWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessSePayWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrganizerWebhook_MissingAllSecurityHeaders_Returns401Unauthorized_AndNeverDispatchesMediator()
    {
        // Arrange: Không có X-Organizer-Secret, X-Organizer-ApiKey hay Authorization
        var (controller, mediatorMock) = CreateOrganizerController();
        var payload = new OrganizerTicketUsedWebhookRequest { TicketCode = "TCK-SEC-001" };

        // Act
        var result = await controller.HandleTicketUsedWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<object>>(unauthorizedResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("không hợp lệ", apiResponse.Message);

        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessTicketUsedWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrganizerWebhook_FakeSecretInXOrganizerSecretHeader_Returns401Unauthorized()
    {
        // Arrange
        var (controller, mediatorMock) = CreateOrganizerController();
        controller.HttpContext.Request.Headers["X-Organizer-Secret"] = "FakeOrganizerSecret_Hacker";
        var payload = new OrganizerTicketUsedWebhookRequest { TicketCode = "TCK-SEC-001" };

        // Act
        var result = await controller.HandleTicketUsedWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessTicketUsedWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrganizerWebhook_FakeSecretInXOrganizerApiKeyHeader_Returns401Unauthorized()
    {
        // Arrange
        var (controller, mediatorMock) = CreateOrganizerController();
        controller.HttpContext.Request.Headers["X-Organizer-ApiKey"] = "FakeApiKey_123456789";
        var payload = new OrganizerTicketUsedWebhookRequest { TicketCode = "TCK-SEC-001" };

        // Act
        var result = await controller.HandleTicketUsedWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessTicketUsedWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrganizerWebhook_FakeSecretInAuthorizationBearerHeader_Returns401Unauthorized()
    {
        // Arrange
        var (controller, mediatorMock) = CreateOrganizerController();
        controller.HttpContext.Request.Headers["Authorization"] = "Bearer ForgedOrganizerBearerSecret";
        var payload = new OrganizerTicketUsedWebhookRequest { TicketCode = "TCK-SEC-001" };

        // Act
        var result = await controller.HandleTicketUsedWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessTicketUsedWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region 2. Fake & Malformed Payloads (Từ chối request webhook có nội dung giả mạo)

    [Fact]
    public async Task SePayWebhook_FakePayload_MissingPaymentReference_ThrowsBusinessRuleViolation()
    {
        // Arrange: Nội dung chuyển khoản rác không chứa mã thanh toán dạng TSXXXXXXXX
        var (db, _, _) = CreateDatabaseFixture();
        var handler = new ProcessSePayWebhookCommandHandler(db);

        var payload = new SePayWebhookRequest
        {
            Id = 10001,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = "CHUYEN TIEN MUA VE CHO BAN ANH KHONG CO MA",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "FAKE_NO_REF"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None));
        Assert.Contains("Không thể tìm thấy mã thanh toán hợp lệ", ex.Message);
    }

    [Theory]
    [InlineData("THANH TOAN TS123")] // Quá ngắn (< 10 ký tự)
    [InlineData("THANH TOAN TS123456789012345")] // Regex chỉ match TS + 8 ký tự
    [InlineData("THANH TOAN TS!@#$%^&*")] // Chứa ký tự đặc biệt
    public async Task SePayWebhook_FakePayload_MalformedPaymentReference_ThrowsBusinessRuleViolationOrNotFound(string fakeContent)
    {
        // Arrange
        var (db, _, _) = CreateDatabaseFixture();
        var handler = new ProcessSePayWebhookCommandHandler(db);

        var payload = new SePayWebhookRequest
        {
            Id = 10002,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = fakeContent,
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "MALFORMED_REF"
        };

        // Act & Assert: Phải bị từ chối bằng BusinessRuleViolationException hoặc NotFoundException
        await Assert.ThrowsAnyAsync<Exception>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None));
    }

    [Fact]
    public async Task SePayWebhook_FakePayload_NonExistentPaymentReference_ThrowsNotFoundException()
    {
        // Arrange: Mã đúng định dạng TS + 8 ký tự nhưng không tồn tại trong hệ thống
        var (db, _, _) = CreateDatabaseFixture(paymentReference: "TSSECURE01");
        var handler = new ProcessSePayWebhookCommandHandler(db);

        var payload = new SePayWebhookRequest
        {
            Id = 10003,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = "THANH TOAN TS99999999 CHO VE KHONG CO THAT",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "NON_EXISTENT_REF"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None));
        Assert.Contains("TS99999999", ex.Message);
    }

    [Fact]
    public async Task SePayWebhook_FakePayload_UnderpaymentAttack_ThrowsBusinessRuleViolationAndEscrowRemainsPending()
    {
        // Arrange: Tấn công trả thiếu tiền (ví dụ vé 1.050.000đ nhưng chỉ chuyển 10.000đ)
        const string paymentRef = "TSUNDER002";
        var (db, listing, escrow) = CreateDatabaseFixture(paymentReference: paymentRef, totalBuyerPaid: 1_050_000m);
        var mockVerification = new Mock<ITicketVerificationService>();
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10004,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentRef}",
            TransferType = "in",
            TransferAmount = 10_000m, // Kẻ tấn công gửi 10.000đ
            ReferenceCode = "UNDERPAYMENT_ATTACK"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None));
        Assert.Contains("nhỏ hơn tổng số tiền cần trả", ex.Message);

        // Verification DB: Escrow vẫn Pending, Listing vẫn Transacting, gRPC không được gọi
        db.ChangeTracker.Clear();
        var currentEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        var currentListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id);

        Assert.Equal(EscrowStatus.Pending, currentEscrow.Status);
        Assert.Equal(ListingStatus.Transacting, currentListing.ListingStatus);
        Assert.Null(currentEscrow.NewTicketCode);

        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SePayWebhook_FakePayload_MoneyDrainAttack_TransferTypeOut_IgnoredWithoutModifyingEscrowOrListing()
    {
        // Arrange: Giả mạo giao dịch tiền ra (transferType = "out") để lừa hệ thống xác nhận thanh toán
        const string paymentRef = "TSDRAIN001";
        var (db, listing, escrow) = CreateDatabaseFixture(paymentReference: paymentRef);
        var mockVerification = new Mock<ITicketVerificationService>();
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10005,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"RUT TIEN {paymentRef}",
            TransferType = "out", // Giao dịch tiền ra
            TransferAmount = 1_050_000m,
            ReferenceCode = "MONEY_DRAIN_ATTACK"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("Ignored", response.Data!.EscrowStatus);
        Assert.Equal("Ignored", response.Data.ListingStatus);
        Assert.Contains("không phải giao dịch tiền vào", response.Message);

        // DB hoàn toàn không bị ảnh hưởng
        db.ChangeTracker.Clear();
        var currentEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Pending, currentEscrow.Status);

        mockVerification.Verify(s => s.TransferOwnershipByListingId(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SePayWebhook_FakePayload_NegativeTransferAmount_IgnoredWithoutModifyingEscrow()
    {
        // Arrange: Số tiền âm
        const string paymentRef = "TSNEGATIVE";
        var (db, _, escrow) = CreateDatabaseFixture(paymentReference: paymentRef);
        var mockVerification = new Mock<ITicketVerificationService>();
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10006,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentRef}",
            TransferType = "",
            TransferAmount = -500_000m, // Số tiền âm
            ReferenceCode = "NEGATIVE_AMOUNT"
        };

        // Act
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("Ignored", response.Data!.EscrowStatus);

        db.ChangeTracker.Clear();
        var currentEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Pending, currentEscrow.Status);
    }

    [Fact]
    public async Task SePayWebhook_FakePayload_TargetingInvalidEscrowStatus_ThrowsBusinessRuleViolation()
    {
        // Arrange: Escrow đang ở trạng thái Disputed hoặc Released không thể nhận thanh toán để Lock
        const string paymentRef = "TSDISPUTE1";
        var (db, _, escrow) = CreateDatabaseFixture(
            paymentReference: paymentRef,
            escrowStatus: EscrowStatus.Disputed,
            listingStatus: ListingStatus.Sold);

        var mockVerification = new Mock<ITicketVerificationService>();
        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10007,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            Content = $"THANH TOAN {paymentRef}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "DISPUTED_ESCROW_TARGET"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None));
        Assert.Contains("không thể khóa", ex.Message);
    }

    [Fact]
    public async Task SePayWebhook_FakePayload_SqlInjectionAttemptInContent_SafelyHandledByParameterization()
    {
        // Arrange: Tấn công SQL Injection trong content chuyển khoản
        const string paymentRef = "TSSQLINJ01";
        var (db, listing, escrow) = CreateDatabaseFixture(paymentReference: paymentRef);

        var mockVerification = new Mock<ITicketVerificationService>();
        mockVerification.Setup(s => s.TransferOwnershipByListingId(
                listing.Id, escrow.BuyerId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOwnershipResponse
            {
                Outcome = TransferOutcome.Transferred,
                NewTicket = new TicketSnapshot { Ticket = new TicketReference { TicketCode = "NEW-TCK-SAFE-01" } }
            });

        var handler = new ProcessSePayWebhookCommandHandler(db, null, null, null, mockVerification.Object);

        var payload = new SePayWebhookRequest
        {
            Id = 10008,
            Gateway = "MBBank",
            TransactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            AccountNumber = "0938434102",
            // Chuỗi tấn công SQL Injection kèm XSS
            Content = $"'; DROP TABLE escrow_transactions; -- <script>alert('xss')</script> {paymentRef}",
            TransferType = "in",
            TransferAmount = 1_050_000m,
            ReferenceCode = "SQLI_TEST_001"
        };

        // Act: Regex trích xuất an toàn TSSECURE01, EF Core tham số hóa an toàn
        var response = await handler.Handle(new ProcessSePayWebhookCommand(payload), CancellationToken.None);

        // Assert: Không bị crash SQL, Escrow được cập nhật an toàn
        Assert.True(response.Success);
        Assert.Equal("Locked", response.Data!.EscrowStatus);

        db.ChangeTracker.Clear();
        var currentEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrow.Id);
        Assert.Equal(EscrowStatus.Locked, currentEscrow.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OrganizerWebhook_FakePayload_EmptyTicketCode_ThrowsBusinessRuleViolation(string emptyCode)
    {
        // Arrange: Organizer webhook gửi ticketCode rỗng
        var (db, _, _) = CreateDatabaseFixture();
        var mockSettler = new Mock<IEscrowPayoutSettler>();
        var handler = new ProcessTicketUsedWebhookCommandHandler(db, mockSettler.Object);

        var command = new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest
        {
            TicketCode = emptyCode
        });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Contains("TicketCode không được để trống", ex.Message);
        mockSettler.Verify(s => s.TrySettleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrganizerWebhook_FakePayload_NonExistentTicketCode_ThrowsNotFoundException()
    {
        // Arrange: Vé giả mạo không tồn tại trong hệ thống
        var (db, _, _) = CreateDatabaseFixture();
        var mockSettler = new Mock<IEscrowPayoutSettler>();
        var handler = new ProcessTicketUsedWebhookCommandHandler(db, mockSettler.Object);

        var command = new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest
        {
            TicketCode = "TCK-FAKE-NON-EXISTENT-999"
        });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Contains("TCK-FAKE-NON-EXISTENT-999", ex.Message);
        mockSettler.Verify(s => s.TrySettleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrganizerWebhook_FakePayload_TargetingPendingEscrow_DoesNotTriggerEarlyPayoutSettlement()
    {
        // Arrange: Vé thuộc Escrow vẫn đang Pending (chưa thanh toán / chưa Locked)
        const string origCode = "TCK-ORIG-SEC-001";
        var (db, _, escrow) = CreateDatabaseFixture(escrowStatus: EscrowStatus.Pending);
        var mockSettler = new Mock<IEscrowPayoutSettler>();
        var handler = new ProcessTicketUsedWebhookCommandHandler(db, mockSettler.Object);

        var command = new ProcessTicketUsedWebhookCommand(new OrganizerTicketUsedWebhookRequest
        {
            TicketCode = origCode
        });

        // Act: Webhook ghi nhận nhưng không giải ngân vì Escrow chưa Locked
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Contains("Pending", response.Message);

        // TrySettle TUYỆT ĐỐI không được gọi
        mockSettler.Verify(s => s.TrySettleAsync(escrow.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
