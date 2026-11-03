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

/// <summary>
/// SCRUM-247: OrdersController.Checkout chặn replay trước Anti-Bot Guard và trước khi trừ tồn kho.
/// </summary>
public sealed class OrdersControllerReplayGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OrganizerDbContext _db;
    private readonly MutableTimeProvider _clock;
    private readonly SessionTokenService _tokens;
    private readonly Mock<IAntiBotGuardService> _guard;
    private readonly OrdersController _controller;
    private readonly TicketTier _vip;
    private readonly TicketTier _ga;

    public OrdersControllerReplayGuardTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new OrganizerDbContext(new DbContextOptionsBuilder<OrganizerDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _vip = NewTier("VIP");
        _ga = NewTier("GA");
        _db.TicketTiers.AddRange(_vip, _ga);
        _db.SaveChanges();

        _clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SessionToken:SecretKey"] = "SecretKey_OrdersController_Replay_Test_Key!"
            })
            .Build();
        _tokens = new SessionTokenService(config, _clock);

        // Guard luôn Fail-Open Allow: mô phỏng Redis/ONNX chậm quá 20ms.
        _guard = new Mock<IAntiBotGuardService>();
        _guard.Setup(g => g.EvaluateCheckoutGuardAsync(It.IsAny<CheckoutRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AntiBotGuardResult
            {
                Decision = AntiBotDecision.Allow,
                IsFailOpen = true,
                Vector = new BotRiskVector(),
                Reason = "Fail-Open: SLA Timeout (> 20ms)"
            });

        var telemetryService = new Mock<IServerTransactionTelemetryService>();
        _controller = new OrdersController(
            _db,
            telemetryService.Object,
            _tokens,
            _guard.Object,
            new ReplayGuardService(_tokens, clock: _clock),
            NullLogger<OrdersController>.Instance,
            clock: _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static TicketTier NewTier(string name) => new()
    {
        Id = Guid.NewGuid(),
        EventId = "ev-replay-247",
        TierName = name,
        Price = 500_000,
        TotalQuantity = 50,
        AvailableQuantity = 50
    };

    private CheckoutRequest Request(TicketTier tier, string? nonce, string? token = null, bool withTelemetry = true) => new()
    {
        TierId = tier.Id,
        Quantity = 1,
        BuyerEmail = "buyer@test.vn",
        SessionToken = token ?? _tokens.GenerateSessionToken(tier.Id),
        Telemetry = withTelemetry
            ? new ClientTelemetryDto
            {
                IsWebdriver = 0,
                ActionIntervalStdDev = 210f,
                DeviceFingerprint = "a1b2c3d4e5f67890abcdef1234567890",
                Nonce = nonce,
                ClientTimestamp = _clock.GetUtcNow().ToUnixTimeMilliseconds()
            }
            : null
    };

    private int Available(TicketTier tier) =>
        _db.TicketTiers.AsNoTracking().Single(t => t.Id == tier.Id).AvailableQuantity;

    private static string? CodeOf(IActionResult result)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var doc = System.Text.Json.JsonSerializer.SerializeToElement(conflict.Value!);
        return doc.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task Checkout_WhenSameNonceReplayedWhileGuardFailsOpen_Returns409ReplayAndKeepsInventory()
    {
        var first = await _controller.Checkout(Request(_ga, "nonce-247-dup"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(first);
        Assert.Equal(49, Available(_ga));

        _clock.Advance(TimeSpan.FromSeconds(2));
        var replay = await _controller.Checkout(Request(_ga, "nonce-247-dup"), CancellationToken.None);

        Assert.Equal(ReplayRejectionCodes.Replay, CodeOf(replay));
        Assert.Equal(49, Available(_ga));
        _guard.Verify(g => g.EvaluateCheckoutGuardAsync(It.IsAny<CheckoutRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Checkout_WhenTokenIssuedForAnotherTier_Returns409TierMismatchWithoutScoring()
    {
        var vipToken = _tokens.GenerateSessionToken(_vip.Id);

        var result = await _controller.Checkout(Request(_ga, "nonce-247-tier", vipToken), CancellationToken.None);

        Assert.Equal(ReplayRejectionCodes.TierMismatch, CodeOf(result));
        Assert.Equal(50, Available(_ga));
        _guard.Verify(g => g.EvaluateCheckoutGuardAsync(It.IsAny<CheckoutRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Checkout_WhenClientTimestampTooOld_Returns409ClockSkew()
    {
        var request = Request(_ga, "nonce-247-skew");
        _clock.Advance(TimeSpan.FromSeconds(6));

        var result = await _controller.Checkout(request, CancellationToken.None);

        Assert.Equal(ReplayRejectionCodes.ClockSkew, CodeOf(result));
        Assert.Equal(50, Available(_ga));
    }

    [Fact]
    public async Task Checkout_WhenNoTelemetry_GoesThroughGuardAsBefore()
    {
        var result = await _controller.Checkout(Request(_ga, nonce: null, withTelemetry: false), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(49, Available(_ga));
        _guard.Verify(g => g.EvaluateCheckoutGuardAsync(It.IsAny<CheckoutRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Checkout_WhenSoldOut_Returns409WithoutCode()
    {
        var tiny = NewTier("Tiny");
        tiny.AvailableQuantity = 0;
        _db.TicketTiers.Add(tiny);
        await _db.SaveChangesAsync();

        var result = await _controller.Checkout(Request(tiny, "nonce-247-soldout"), CancellationToken.None);

        Assert.Null(CodeOf(result));
    }
}
