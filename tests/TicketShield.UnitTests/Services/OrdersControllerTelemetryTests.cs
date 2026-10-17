using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MockOrganizer.API.AntiBotScoring;
using MockOrganizer.API.Controllers;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;
using MockOrganizer.API.Models;
using MockOrganizer.API.Services;
using Moq;
using Xunit;

namespace TicketShield.UnitTests.Services;

public sealed class OrdersControllerTelemetryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OrganizerDbContext _db;
    private readonly ISessionTokenService _sessionTokenService;
    private readonly IRedisSlidingWindowService _slidingWindow;
    private readonly IServerTransactionTelemetryService _telemetryService;
    private readonly OrdersController _controller;

    public OrdersControllerTelemetryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OrganizerDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new OrganizerDbContext(options);
        _db.Database.EnsureCreated();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SessionToken:SecretKey"] = "SecretKey_OrdersController_Telemetry_Test_Key!"
            })
            .Build();

        _sessionTokenService = new SessionTokenService(config);
        _slidingWindow = new RedisSlidingWindowService();
        _telemetryService = new ServerTransactionTelemetryService(_db, _sessionTokenService, _slidingWindow);

        var guardMock = new Mock<IAntiBotGuardService>();
        guardMock.Setup(g => g.EvaluateCheckoutGuardAsync(
                It.IsAny<CheckoutRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckoutRequest req, string? ip, CancellationToken ct) =>
            {
                var telem = _telemetryService.MeasureAsync(req.SessionToken, req.BuyerEmail, req.ResolvedDeviceFingerprint, ip, ct).GetAwaiter().GetResult();
                return new AntiBotGuardResult
                {
                    Decision = AntiBotDecision.Allow,
                    RiskScore = 0.1f,
                    IsFailOpen = false,
                    ExecutionDurationMs = 0.5,
                    Vector = new BotRiskVector
                    {
                        TimeToCheckout = (float)telem.TimeToCheckoutSeconds,
                        ActionIntervalStdDev = 200f,
                        IsWebDriver = 0f,
                        DeviceAccountOverlap = (float)telem.DeviceAccountOverlap,
                        BurstRate10s = (float)telem.BurstRate10s,
                        ConcurrentHold = (float)telem.ConcurrentHoldCount
                    },
                    Telemetry = telem,
                    Reason = string.Empty
                };
            });

        _controller = new OrdersController(
            _db,
            _telemetryService,
            _sessionTokenService,
            guardMock.Object,
            NullLogger<OrdersController>.Instance,
            _slidingWindow
        );
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Checkout_UpdatesSlidingWindowHold_AndFeedsIntoNextCheckoutTelemetry()
    {
        // 1. Chuẩn bị TicketTier có sẵn 100 vé
        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = "ev-test-101",
            TierName = "VIP Standing",
            Price = 1_500_000,
            TotalQuantity = 100,
            AvailableQuantity = 100
        };
        _db.TicketTiers.Add(tier);
        await _db.SaveChangesAsync();

        var buyerEmail = "flashsale_buyer@ticketshield.vn";
        var token = _sessionTokenService.GenerateSessionToken(tier.Id);

        // 2. Lần Checkout đầu tiên: Buyer chưa giữ vé nào (concurrent_hold = 0)
        var req1 = new CheckoutRequest
        {
            TierId = tier.Id,
            Quantity = 2,
            BuyerEmail = buyerEmail,
            SessionToken = token
        };

        var result1 = await _controller.Checkout(req1, CancellationToken.None);
        var okResult1 = Assert.IsType<OkObjectResult>(result1);
        var doc1 = System.Text.Json.JsonSerializer.SerializeToElement(okResult1.Value!);

        Assert.Equal(0, doc1.GetProperty("telemetry").GetProperty("concurrentHoldCount").GetInt32());

        // Kiểm tra sau lần 1, Redis sliding window store đã ghi nhận 2 vé đang giữ
        var holdAfterReq1 = await _slidingWindow.GetConcurrentHoldAsync(buyerEmail);
        Assert.Equal(2, holdAfterReq1);

        // 3. Lần Checkout thứ hai: Buyer tiếp tục checkout 3 vé
        var req2 = new CheckoutRequest
        {
            TierId = tier.Id,
            Quantity = 3,
            BuyerEmail = buyerEmail,
            SessionToken = token
        };

        var result2 = await _controller.Checkout(req2, CancellationToken.None);
        var okResult2 = Assert.IsType<OkObjectResult>(result2);
        var doc2 = System.Text.Json.JsonSerializer.SerializeToElement(okResult2.Value!);

        // Telemetry lần này PHẢI đọc được concurrentHoldCount = 2 từ sliding window store!
        Assert.Equal(2, doc2.GetProperty("telemetry").GetProperty("concurrentHoldCount").GetInt32());

        // Sau lần 2, store phải tăng lũy tiến lên 5 vé đang giữ
        var holdAfterReq2 = await _slidingWindow.GetConcurrentHoldAsync(buyerEmail);
        Assert.Equal(5, holdAfterReq2);
    }
}
