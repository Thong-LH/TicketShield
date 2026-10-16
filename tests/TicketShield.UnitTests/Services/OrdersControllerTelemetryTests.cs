using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MockOrganizer.API.Controllers;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;
using MockOrganizer.API.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

public sealed class OrdersControllerTelemetryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OrganizerDbContext _db;
    private readonly ISessionTokenService _sessionTokenService;
    private readonly IPipelineHoldTracker _holdTracker;
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
        _holdTracker = new InMemoryPipelineHoldTracker();
        _telemetryService = new ServerTransactionTelemetryService(_holdTracker, _sessionTokenService);

        _controller = new OrdersController(
            _db,
            _telemetryService,
            _sessionTokenService,
            _holdTracker,
            NullLogger<OrdersController>.Instance
        );
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Checkout_UpdatesPipelineHoldTracker_AndFeedsIntoNextCheckoutTelemetry()
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

        // Kiểm tra sau lần 1, cache đã ghi nhận 2 vé đang giữ
        var holdAfterReq1 = await _holdTracker.GetHoldCountAsync(buyerEmail);
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

        // Telemetry lần này PHẢI đọc được concurrentHoldCount = 2 từ cache pipeline!
        Assert.Equal(2, doc2.GetProperty("telemetry").GetProperty("concurrentHoldCount").GetInt32());

        // Sau lần 2, cache phải tăng lũy tiến lên 5 vé đang giữ
        var holdAfterReq2 = await _holdTracker.GetHoldCountAsync(buyerEmail);
        Assert.Equal(5, holdAfterReq2);
    }
}
