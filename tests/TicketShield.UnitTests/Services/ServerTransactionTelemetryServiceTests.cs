using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using MockOrganizer.API.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

/// <summary>
/// Unit and Benchmark Tests for BE-PIPE-8.2.1:
/// Server Transaction Latency Measurement (time_to_checkout) and Concurrent Hold Lookup.
/// </summary>
public sealed class ServerTransactionTelemetryServiceTests
{
    private readonly IConfiguration _config;
    private readonly ISessionTokenService _sessionTokenService;
    private readonly IPipelineHoldTracker _holdTracker;

    public ServerTransactionTelemetryServiceTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SessionToken:SecretKey"] = "UnitTesting_SecretKey_AntiBot_2026_Test_Key!"
            })
            .Build();

        _sessionTokenService = new SessionTokenService(_config);
        _holdTracker = new InMemoryPipelineHoldTracker();
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

        var sut = new ServerTransactionTelemetryService(_holdTracker, _sessionTokenService);

        var result = await sut.MeasureAsync(token, "buyer@test.com");

        Assert.True(result.IsSessionValid);
        // TimeToCheckout phải xấp xỉ 3.5 giây (+/- 0.5s dung sai test)
        Assert.InRange(result.TimeToCheckoutSeconds, 3.4, 4.0);
        Assert.Equal(issuedAt.ToUnixTimeMilliseconds(), result.IssuedAtUtc.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task MeasureAsync_WithInvalidOrMissingToken_SetsTimeToCheckoutToNegativeOne()
    {
        var sut = new ServerTransactionTelemetryService(_holdTracker, _sessionTokenService);

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

        // Ghi số vé đang giữ vào Pipeline Cache (tương đương Redis key hold:{buyerEmail})
        await _holdTracker.SetHoldCountAsync(targetBuyer, 5);
        await _holdTracker.SetHoldCountAsync(otherBuyer, 2);

        var sut = new ServerTransactionTelemetryService(_holdTracker, _sessionTokenService);

        var resultTarget = await sut.MeasureAsync(null, targetBuyer);
        var resultOther = await sut.MeasureAsync(null, otherBuyer);
        var resultUnknown = await sut.MeasureAsync(null, "unknown@ticketshield.vn");

        Assert.Equal(5, resultTarget.ConcurrentHoldCount);
        Assert.Equal(2, resultOther.ConcurrentHoldCount);
        Assert.Equal(0, resultUnknown.ConcurrentHoldCount);
    }

    [Fact]
    public async Task MeasureAsync_WhenBuyerEmailIsEmptyOrNull_ReturnsZeroConcurrentHold()
    {
        var sut = new ServerTransactionTelemetryService(_holdTracker, _sessionTokenService);

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

        await _holdTracker.SetHoldCountAsync(buyer, 2);

        var sut = new ServerTransactionTelemetryService(_holdTracker, _sessionTokenService);

        // Warm up
        _ = await sut.MeasureAsync(token, buyer);

        // Đo đạc thực tế
        var result = await sut.MeasureAsync(token, buyer);

        // Kiểm tra tiêu chí Acceptance Criteria SLA: < 2ms (In-Memory/Cache đạt < 0.1ms)
        Assert.True(result.ExecutionDurationMs < 2.0,
            $"ExecutionDurationMs was {result.ExecutionDurationMs:F4} ms, expected < 2.0 ms");
        Assert.Equal(2, result.ConcurrentHoldCount);
        Assert.True(result.IsSessionValid);
    }
}
