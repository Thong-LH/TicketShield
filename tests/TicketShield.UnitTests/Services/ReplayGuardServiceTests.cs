using Microsoft.Extensions.Configuration;
using MockOrganizer.API.Controllers;
using MockOrganizer.API.Models;
using MockOrganizer.API.Services;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace TicketShield.UnitTests.Services;

/// <summary>
/// Unit Tests cho SCRUM-247: chặn replay checkout bằng nonce, độ lệch giờ 5 giây và khớp hạng vé.
/// </summary>
public sealed class ReplayGuardServiceTests
{
    private static readonly Guid VipTier = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GaTier = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly MutableTimeProvider _clock;
    private readonly IConfiguration _config;
    private readonly SessionTokenService _tokens;

    public ReplayGuardServiceTests()
    {
        _clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero));
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SessionToken:SecretKey"] = "SecretKey_ReplayGuard_Test_Key_2026!",
                ["Redis:InstanceName"] = "test:ts:"
            })
            .Build();
        _tokens = new SessionTokenService(_config, _clock);
    }

    private ReplayGuardService CreateService(IConnectionMultiplexer? redis = null) =>
        new(_tokens, redis, _config, _clock);

    private long NowMs => _clock.GetUtcNow().ToUnixTimeMilliseconds();

    private CheckoutRequest Request(
        Guid tierId,
        string? sessionToken = null,
        string? nonce = null,
        long? clientTimestamp = null,
        bool withTelemetry = true) => new()
    {
        TierId = tierId,
        Quantity = 1,
        BuyerEmail = "buyer@test.vn",
        SessionToken = sessionToken,
        Telemetry = withTelemetry
            ? new ClientTelemetryDto { IsWebdriver = 0, Nonce = nonce, ClientTimestamp = clientTimestamp }
            : null
    };

    // =========================================================================
    // 1. Khớp hạng vé
    // =========================================================================

    [Fact]
    public async Task Validate_WhenTokenIssuedForAnotherTier_RejectsTierMismatch()
    {
        var vipToken = _tokens.GenerateSessionToken(VipTier);

        var result = await CreateService().ValidateAsync(Request(GaTier, vipToken, "n-1", NowMs));

        Assert.False(result.IsAllowed);
        Assert.Equal(ReplayRejectionCodes.TierMismatch, result.Code);
    }

    [Fact]
    public async Task Validate_WhenTokenMatchesTierAndNoTelemetry_Allows()
    {
        var gaToken = _tokens.GenerateSessionToken(GaTier);

        var result = await CreateService().ValidateAsync(Request(GaTier, gaToken, withTelemetry: false));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task Validate_WhenTokenIsInvalid_SkipsTierCheck()
    {
        var result = await CreateService().ValidateAsync(Request(GaTier, "garbage.token", withTelemetry: false));

        Assert.True(result.IsAllowed);
    }

    // =========================================================================
    // 2. Client cũ không gửi nonce
    // =========================================================================

    [Fact]
    public async Task Validate_WhenTelemetryHasNoNonce_SkipsClockAndNonceChecks()
    {
        var result = await CreateService().ValidateAsync(Request(GaTier, nonce: null, clientTimestamp: null));

        Assert.True(result.IsAllowed);
    }

    // =========================================================================
    // 3. Định dạng nonce
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_WhenNonceIsBlank_RejectsReplay(string nonce)
    {
        var result = await CreateService().ValidateAsync(Request(GaTier, nonce: nonce, clientTimestamp: NowMs));

        Assert.Equal(ReplayRejectionCodes.Replay, result.Code);
    }

    [Fact]
    public async Task Validate_WhenNonceLongerThan64Chars_RejectsReplay()
    {
        var result = await CreateService().ValidateAsync(Request(GaTier, nonce: new string('a', 65), clientTimestamp: NowMs));

        Assert.Equal(ReplayRejectionCodes.Replay, result.Code);
    }

    // =========================================================================
    // 4. Độ lệch giờ 5 giây
    // =========================================================================

    [Fact]
    public async Task Validate_WhenNonceWithoutClientTimestamp_RejectsClockSkew()
    {
        var result = await CreateService().ValidateAsync(Request(GaTier, nonce: "n-no-ts", clientTimestamp: null));

        Assert.Equal(ReplayRejectionCodes.ClockSkew, result.Code);
    }

    [Theory]
    [InlineData(-6000)]
    [InlineData(6000)]
    public async Task Validate_WhenClientTimestampOffBySixSeconds_RejectsClockSkew(long offsetMs)
    {
        var result = await CreateService().ValidateAsync(Request(GaTier, nonce: $"n-skew-{offsetMs}", clientTimestamp: NowMs + offsetMs));

        Assert.Equal(ReplayRejectionCodes.ClockSkew, result.Code);
    }

    [Theory]
    [InlineData(-4000)]
    [InlineData(4000)]
    public async Task Validate_WhenClientTimestampOffByFourSeconds_Allows(long offsetMs)
    {
        var result = await CreateService().ValidateAsync(Request(GaTier, nonce: $"n-ok-{offsetMs}", clientTimestamp: NowMs + offsetMs));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task Validate_WhenRejectedForClockSkew_DoesNotConsumeNonce()
    {
        var service = CreateService();

        var skewed = await service.ValidateAsync(Request(GaTier, nonce: "n-retry", clientTimestamp: NowMs - 10_000));
        var retried = await service.ValidateAsync(Request(GaTier, nonce: "n-retry", clientTimestamp: NowMs));

        Assert.Equal(ReplayRejectionCodes.ClockSkew, skewed.Code);
        Assert.True(retried.IsAllowed);
    }

    // =========================================================================
    // 5. Nonce dùng một lần trong 10 phút (dự phòng bộ nhớ)
    // =========================================================================

    [Fact]
    public async Task Validate_WhenSameNonceSentTwice_RejectsSecondAsReplay()
    {
        var service = CreateService();

        var first = await service.ValidateAsync(Request(GaTier, nonce: "n-dup", clientTimestamp: NowMs));
        _clock.Advance(TimeSpan.FromSeconds(2));
        var second = await service.ValidateAsync(Request(GaTier, nonce: "n-dup", clientTimestamp: NowMs));

        Assert.True(first.IsAllowed);
        Assert.Equal(ReplayRejectionCodes.Replay, second.Code);
    }

    [Fact]
    public async Task Validate_WhenNonceOlderThanTenMinutes_AllowsAgain()
    {
        var service = CreateService();

        await service.ValidateAsync(Request(GaTier, nonce: "n-expire", clientTimestamp: NowMs));
        _clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        var later = await service.ValidateAsync(Request(GaTier, nonce: "n-expire", clientTimestamp: NowMs));

        Assert.True(later.IsAllowed);
    }

    [Fact]
    public async Task Validate_WhenSameNonceRacesInParallel_ExactlyOneIsAllowed()
    {
        var service = CreateService();
        var ts = NowMs;

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => service.ValidateAsync(Request(GaTier, nonce: "n-race", clientTimestamp: ts)))));

        Assert.Equal(1, results.Count(r => r.IsAllowed));
        Assert.All(results.Where(r => !r.IsAllowed), r => Assert.Equal(ReplayRejectionCodes.Replay, r.Code));
    }

    // =========================================================================
    // 6. Redis
    // =========================================================================

    private static (Mock<IConnectionMultiplexer> Redis, Mock<IDatabase> Db) MockRedis()
    {
        var db = new Mock<IDatabase>();
        var redis = new Mock<IConnectionMultiplexer>();
        redis.SetupGet(r => r.IsConnected).Returns(true);
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(db.Object);
        return (redis, db);
    }

    [Fact]
    public async Task Validate_WhenRedisReportsKeyExists_RejectsReplay()
    {
        var (redis, db) = MockRedis();
        db.SetupSequence(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists, CommandFlags.None))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        var service = CreateService(redis.Object);

        var first = await service.ValidateAsync(Request(GaTier, nonce: "n-redis", clientTimestamp: NowMs));
        var second = await service.ValidateAsync(Request(GaTier, nonce: "n-redis", clientTimestamp: NowMs));

        Assert.True(first.IsAllowed);
        Assert.Equal(ReplayRejectionCodes.Replay, second.Code);
        db.Verify(d => d.StringSetAsync(
            (RedisKey)"test:ts:nonce:n-redis", (RedisValue)"1", TimeSpan.FromMinutes(10), When.NotExists, CommandFlags.None), Times.Exactly(2));
    }

    [Fact]
    public async Task Validate_WhenRedisThrows_FallsBackToMemoryAndStillRejectsDuplicate()
    {
        var (redis, db) = MockRedis();
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var service = CreateService(redis.Object);

        var first = await service.ValidateAsync(Request(GaTier, nonce: "n-down", clientTimestamp: NowMs));
        var second = await service.ValidateAsync(Request(GaTier, nonce: "n-down", clientTimestamp: NowMs));

        Assert.True(first.IsAllowed);
        Assert.Equal(ReplayRejectionCodes.Replay, second.Code);
    }
}
