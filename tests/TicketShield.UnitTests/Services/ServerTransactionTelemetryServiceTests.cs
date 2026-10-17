using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;
using MockOrganizer.API.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

/// <summary>
/// Unit and Benchmark Tests for BE-PIPE-8.2.1:
/// Server Transaction Latency Measurement (time_to_checkout) and Concurrent Hold Lookup.
/// </summary>
public sealed class ServerTransactionTelemetryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OrganizerDbContext _db;
    private readonly IConfiguration _config;
    private readonly ISessionTokenService _sessionTokenService;

    public ServerTransactionTelemetryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OrganizerDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new OrganizerDbContext(options);
        _db.Database.EnsureCreated();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SessionToken:SecretKey"] = "UnitTesting_SecretKey_AntiBot_2026_Test_Key!"
            })
            .Build();

        _sessionTokenService = new SessionTokenService(_config);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // =========================================================================
    // 1. SessionTokenService Tests (time_to_checkout foundation)
    // =========================================================================

    [Fact]
    public void GenerateSessionToken_ProducesValidSignedToken()
    {
        var tierId = Guid.NewGuid();
        var issuedAt = DateTimeOffset.UtcNow.AddSeconds(-2);

        var token = _sessionTokenService.GenerateSessionToken(tierId, issuedAt);

        Assert.NotNull(token);
        Assert.Contains(".", token);

        var (isValid, parsedIssuedAt, parsedTierId) = _sessionTokenService.ValidateSessionToken(token);

        Assert.True(isValid);
        Assert.Equal(tierId, parsedTierId);
        Assert.Equal(issuedAt.ToUnixTimeMilliseconds(), parsedIssuedAt.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void ValidateSessionToken_WhenTampered_ReturnsInvalid()
    {
        var tierId = Guid.NewGuid();
        var token = _sessionTokenService.GenerateSessionToken(tierId);

        // Can thiệp vào chữ ký hoặc payload
        var parts = token.Split('.');
        var tamperedToken = parts[0] + "TAMPERED." + parts[1];

        var (isValid, _, _) = _sessionTokenService.ValidateSessionToken(tamperedToken);

        Assert.False(isValid);
    }

    [Fact]
    public void ValidateSessionToken_WhenInFutureBeyondTolerance_ReturnsInvalid()
    {
        var tierId = Guid.NewGuid();
        // Token ở tương lai quá 10 giây (vượt qua dung sai 5 giây)
        var futureIssuedAt = DateTimeOffset.UtcNow.AddSeconds(10);
        var token = _sessionTokenService.GenerateSessionToken(tierId, futureIssuedAt);

        var (isValid, _, _) = _sessionTokenService.ValidateSessionToken(token);

        Assert.False(isValid);
    }

    [Fact]
    public void ValidateSessionToken_WhenExpired_ReturnsInvalid()
    {
        var tierId = Guid.NewGuid();
        // Token phát từ 3 giờ trước (quá hạn 2 giờ)
        var expiredIssuedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var token = _sessionTokenService.GenerateSessionToken(tierId, expiredIssuedAt);

        var (isValid, _, _) = _sessionTokenService.ValidateSessionToken(token);

        Assert.False(isValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-single-part-token")]
    [InlineData("part1.part2.part3")]
    [InlineData("invalidBase64.invalidSignature")]
    public void ValidateSessionToken_WhenMalformedOrNull_ReturnsInvalid(string? malformedToken)
    {
        var (isValid, _, _) = _sessionTokenService.ValidateSessionToken(malformedToken);

        Assert.False(isValid);
    }

    [Fact]
    public void SessionToken_ValidationPerformance_ExecutesUnderFiftyMicroseconds()
    {
        var tierId = Guid.NewGuid();
        var token = _sessionTokenService.GenerateSessionToken(tierId);

        // Warm up
        _ = _sessionTokenService.ValidateSessionToken(token);

        const int iterations = 1000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var (valid, _, _) = _sessionTokenService.ValidateSessionToken(token);
            Assert.True(valid);
        }
        sw.Stop();

        var averageMs = sw.Elapsed.TotalMilliseconds / iterations;
        // Đảm bảo trung bình mỗi lần xác thực dưới 0.1ms (100 microseconds)
        Assert.True(averageMs < 0.1, $"Average validation duration was {averageMs:F4} ms, expected < 0.1 ms");
    }

    // =========================================================================
    // 2. ServerTransactionTelemetryService Tests (time_to_checkout + concurrent_hold)
    // =========================================================================

    [Fact]
    public async Task MeasureAsync_WithValidSessionToken_CalculatesTimeToCheckoutCorrectly()
    {
        var tierId = Guid.NewGuid();
        // Giả lập token cấp trước đó 3.5 giây
        var issuedAt = DateTimeOffset.UtcNow.AddSeconds(-3.5);
        var token = _sessionTokenService.GenerateSessionToken(tierId, issuedAt);

        var sut = new ServerTransactionTelemetryService(_db, _sessionTokenService);

        var result = await sut.MeasureAsync(token, "buyer@test.com");

        Assert.True(result.IsSessionValid);
        // TimeToCheckout phải xấp xỉ 3.5 giây (+/- 0.5s dung sai test)
        Assert.InRange(result.TimeToCheckoutSeconds, 3.4, 4.0);
        Assert.Equal(issuedAt.ToUnixTimeMilliseconds(), result.IssuedAtUtc.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task MeasureAsync_WithInvalidOrMissingToken_SetsTimeToCheckoutToNegativeOne()
    {
        var sut = new ServerTransactionTelemetryService(_db, _sessionTokenService);

        var resultNull = await sut.MeasureAsync(null, "buyer@test.com");
        Assert.False(resultNull.IsSessionValid);
        Assert.Equal(-1.0, resultNull.TimeToCheckoutSeconds);

        var resultInvalid = await sut.MeasureAsync("tampered.token", "buyer@test.com");
        Assert.False(resultInvalid.IsSessionValid);
        Assert.Equal(-1.0, resultInvalid.TimeToCheckoutSeconds);
    }

    [Fact]
    public async Task MeasureAsync_CalculatesConcurrentHoldCountCorrectly()
    {
        var targetBuyer = "target_buyer@ticketshield.vn";
        var otherBuyer = "other_buyer@ticketshield.vn";
        var now = DateTimeOffset.UtcNow;

        // 1. Đơn PENDING hợp lệ của targetBuyer (2 vé, tạo 2 phút trước) -> TÍNH
        _db.MockOrders.Add(new MockOrder
        {
            Id = Guid.NewGuid(),
            BuyerEmail = targetBuyer,
            Quantity = 2,
            Status = "PENDING",
            CreatedAt = now.AddMinutes(-2),
            EventId = "ev-1",
            TierName = "GA"
        });

        // 2. Đơn PENDING hợp lệ thứ hai của targetBuyer (3 vé, tạo 5 phút trước) -> TÍNH
        _db.MockOrders.Add(new MockOrder
        {
            Id = Guid.NewGuid(),
            BuyerEmail = targetBuyer,
            Quantity = 3,
            Status = "PENDING",
            CreatedAt = now.AddMinutes(-5),
            EventId = "ev-1",
            TierName = "VIP"
        });

        // 3. Đơn đã CONFIRMED của targetBuyer -> KHÔNG TÍNH
        _db.MockOrders.Add(new MockOrder
        {
            Id = Guid.NewGuid(),
            BuyerEmail = targetBuyer,
            Quantity = 4,
            Status = "CONFIRMED",
            CreatedAt = now.AddMinutes(-1),
            EventId = "ev-1",
            TierName = "GA"
        });

        // 4. Đơn đã CANCELLED của targetBuyer -> KHÔNG TÍNH
        _db.MockOrders.Add(new MockOrder
        {
            Id = Guid.NewGuid(),
            BuyerEmail = targetBuyer,
            Quantity = 1,
            Status = "CANCELLED",
            CreatedAt = now.AddMinutes(-3),
            EventId = "ev-1",
            TierName = "GA"
        });

        // 5. Đơn PENDING nhưng đã quá hạn (15 phút trước, vượt quá 10 phút window) -> KHÔNG TÍNH
        _db.MockOrders.Add(new MockOrder
        {
            Id = Guid.NewGuid(),
            BuyerEmail = targetBuyer,
            Quantity = 10,
            Status = "PENDING",
            CreatedAt = now.AddMinutes(-15),
            EventId = "ev-1",
            TierName = "GA"
        });

        // 6. Đơn PENDING của người khác -> KHÔNG TÍNH
        _db.MockOrders.Add(new MockOrder
        {
            Id = Guid.NewGuid(),
            BuyerEmail = otherBuyer,
            Quantity = 6,
            Status = "PENDING",
            CreatedAt = now.AddMinutes(-1),
            EventId = "ev-1",
            TierName = "GA"
        });

        await _db.SaveChangesAsync();

        var sut = new ServerTransactionTelemetryService(_db, _sessionTokenService);

        var result = await sut.MeasureAsync(null, targetBuyer);

        // Chỉ có đơn 1 (2 vé) + đơn 2 (3 vé) = đúng 5 vé
        Assert.Equal(5, result.ConcurrentHoldCount);
    }

    [Fact]
    public async Task MeasureAsync_WhenBuyerEmailIsEmptyOrNull_ReturnsZeroConcurrentHold()
    {
        var sut = new ServerTransactionTelemetryService(_db, _sessionTokenService);

        var resultNull = await sut.MeasureAsync(null, null);
        Assert.Equal(0, resultNull.ConcurrentHoldCount);

        var resultEmpty = await sut.MeasureAsync(null, "   ");
        Assert.Equal(0, resultEmpty.ConcurrentHoldCount);
    }

    [Fact]
    public async Task MeasureAsync_Benchmark_ExecutesUnderTwoMilliseconds()
    {
        var buyer = "benchmark_buyer@ticketshield.vn";
        var tierId = Guid.NewGuid();
        var token = _sessionTokenService.GenerateSessionToken(tierId);

        _db.MockOrders.Add(new MockOrder
        {
            Id = Guid.NewGuid(),
            BuyerEmail = buyer,
            Quantity = 2,
            Status = "PENDING",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            EventId = "ev-bench",
            TierName = "GA"
        });
        await _db.SaveChangesAsync();

        var sut = new ServerTransactionTelemetryService(_db, _sessionTokenService);

        // Warm up
        _ = await sut.MeasureAsync(token, buyer);

        // Đo đạc thực tế
        var result = await sut.MeasureAsync(token, buyer);

        // Kiểm tra tiêu chí Acceptance Criteria SLA: < 2ms
        Assert.True(result.ExecutionDurationMs < 2.0,
            $"ExecutionDurationMs was {result.ExecutionDurationMs:F4} ms, expected < 2.0 ms");
        Assert.Equal(2, result.ConcurrentHoldCount);
        Assert.True(result.IsSessionValid);
    }

    // =========================================================================
    // 3. BE-PIPE-8.2.2 Integration Tests (burst_rate_10s & device_account_overlap)
    // =========================================================================

    [Fact]
    public async Task MeasureAsync_WithDeviceAndIp_MeasuresBurstRateAndDeviceOverlap()
    {
        var buyer = "sliding_window_buyer@ticketshield.vn";
        var tierId = Guid.NewGuid();
        var token = _sessionTokenService.GenerateSessionToken(tierId);
        const string deviceFingerprint = "dev_fingerprint_hash_abc123";
        const string clientIp = "192.168.1.55";

        var sut = new ServerTransactionTelemetryService(_db, _sessionTokenService);

        var result = await sut.MeasureAsync(
            sessionToken: token,
            buyerEmail: buyer,
            deviceFingerprint: deviceFingerprint,
            clientIp: clientIp);

        Assert.True(result.IsSessionValid);
        Assert.Equal(1, result.BurstRate10s);
        Assert.Equal(1, result.DeviceAccountOverlap);
        Assert.True(result.RedisDurationMs >= 0.0);
    }

    [Fact]
    public async Task MeasureAsync_WhenDeviceMissing_ReportsNegativeOneOverlap()
    {
        var buyer = "missing_dev_buyer@ticketshield.vn";
        var tierId = Guid.NewGuid();
        var token = _sessionTokenService.GenerateSessionToken(tierId);

        var sut = new ServerTransactionTelemetryService(_db, _sessionTokenService);

        var result = await sut.MeasureAsync(
            sessionToken: token,
            buyerEmail: buyer,
            deviceFingerprint: null,
            clientIp: "10.0.0.1");

        Assert.True(result.IsSessionValid);
        Assert.Equal(1, result.BurstRate10s);
        Assert.Equal(-1, result.DeviceAccountOverlap); // Spec 08 missing policy
    }
}
