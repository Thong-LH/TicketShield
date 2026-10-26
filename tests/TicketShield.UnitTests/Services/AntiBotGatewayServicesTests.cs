using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TicketShield.AntiBot.Gateway.Models;
using TicketShield.AntiBot.Gateway.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

public class AntiBotGatewayServicesTests
{
    private readonly IConfiguration _config;
    private readonly MutableTimeProvider _clock;

    public AntiBotGatewayServicesTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SessionToken:SecretKey"] = "Test_SecretKey_For_UnitTesting_AntiBot_Gateway_2026!",
                ["AntiBotScoring:AllowThreshold"] = "0.35",
                ["AntiBotScoring:BlockThreshold"] = "0.75",
                ["AntiBotScoring:GuardTimeoutMs"] = "20"
            })
            .Build();

        _clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void SessionTokenService_GenerateAndValidate_Valid_ReturnsTrue()
    {
        // Arrange
        var service = new SessionTokenService(_config, _clock);
        var tierId = Guid.NewGuid();

        // Act
        var token = service.GenerateSessionToken(tierId);
        var (isValid, issuedAt, parsedTierId) = service.ValidateSessionToken(token);

        // Assert
        Assert.True(isValid);
        Assert.Equal(tierId, parsedTierId);
        Assert.Equal(_clock.GetUtcNow(), issuedAt);
    }

    [Fact]
    public void SessionTokenService_Validate_TamperedSignature_ReturnsFalse()
    {
        // Arrange
        var service = new SessionTokenService(_config, _clock);
        var token = service.GenerateSessionToken(Guid.NewGuid());

        // Act: Làm giả chữ ký
        var parts = token.Split('.');
        var tamperedToken = parts[0] + ".TamperedSignatureXYZ";
        var (isValid, _, _) = service.ValidateSessionToken(tamperedToken);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void SessionTokenService_Validate_ExpiredToken_ReturnsFalse()
    {
        // Arrange
        var service = new SessionTokenService(_config, _clock);
        var token = service.GenerateSessionToken(Guid.NewGuid());

        // Tua đồng hồ quá 2 giờ
        _clock.Advance(TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(1)));

        // Act
        var (isValid, _, _) = service.ValidateSessionToken(token);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ScoringService_HumanProfile_ReturnsAllow()
    {
        // Arrange
        var options = Options.Create(new AntiBotScoringOptions
        {
            AllowThreshold = 0.35f,
            BlockThreshold = 0.75f
        });
        var logger = NullLogger<AntiBotScoringService>.Instance;
        var scoring = new AntiBotScoringService(options, logger);

        var humanVector = new BotRiskVector
        {
            TimeToCheckout = 15.5f,
            ActionIntervalStdDev = 150.0f,
            BurstRate10s = 1.0f,
            ConcurrentHold = 0.0f,
            IsWebDriver = 0.0f,
            DeviceAccountOverlap = 0.0f
        };

        // Act
        var result = scoring.Evaluate(humanVector);

        // Assert
        Assert.Equal("Allow", result.Decision);
        Assert.True(result.RiskScore < 0.35f);
    }

    [Fact]
    public void ScoringService_AggressiveBotProfile_ReturnsBlock()
    {
        // Arrange
        var options = Options.Create(new AntiBotScoringOptions
        {
            AllowThreshold = 0.35f,
            BlockThreshold = 0.75f
        });
        var logger = NullLogger<AntiBotScoringService>.Instance;
        var scoring = new AntiBotScoringService(options, logger);

        var botVector = new BotRiskVector
        {
            TimeToCheckout = 0.2f,
            ActionIntervalStdDev = 5.0f,
            BurstRate10s = 25.0f,
            ConcurrentHold = 8.0f,
            IsWebDriver = 1.0f,
            DeviceAccountOverlap = 1.0f
        };

        // Act
        var result = scoring.Evaluate(botVector);

        // Assert
        Assert.Equal("Block", result.Decision);
        Assert.True(result.RiskScore >= 0.75f);
    }

    [Fact]
    public async Task RedisService_AtomicHold_IncrementsSequentiallyInMemory()
    {
        // Arrange
        var service = new AntiBotRedisService(null, _config, _clock);
        var email = "buyer@ticketshield.vn";

        // Act: 3 lượt hold liên tiếp
        var r1 = await service.MeasureTelemetryAsync("127.0.0.1", "fp_123", email);
        var r2 = await service.MeasureTelemetryAsync("127.0.0.1", "fp_123", email);
        var r3 = await service.MeasureTelemetryAsync("127.0.0.1", "fp_123", email);

        // Assert: Do là đếm lạc quan nguyên tử (đọc trước khi ghi nhận lượt hiện tại)
        Assert.Equal(0.0f, r1.ConcurrentHold);
        Assert.Equal(1.0f, r2.ConcurrentHold);
        Assert.Equal(2.0f, r3.ConcurrentHold);
    }

    [Fact]
    public async Task AntiBotGuardService_NormalEvaluation_Succeeds()
    {
        // Arrange
        var tokenService = new SessionTokenService(_config, _clock);
        var redisService = new AntiBotRedisService(null, _config, _clock);
        var options = Options.Create(new AntiBotScoringOptions
        {
            AllowThreshold = 0.35f,
            BlockThreshold = 0.75f,
            GuardTimeoutMs = 50
        });
        var scoring = new AntiBotScoringService(options, NullLogger<AntiBotScoringService>.Instance);
        var guard = new AntiBotGuardService(
            tokenService, redisService, scoring, options, NullLogger<AntiBotGuardService>.Instance, _clock);

        var tierId = Guid.NewGuid();
        var token = tokenService.GenerateSessionToken(tierId);
        _clock.Advance(TimeSpan.FromSeconds(12));

        // Act
        var result = await guard.EvaluateCheckoutGuardAsync(
            sessionToken: token,
            buyerEmail: "human@ticketshield.vn",
            clientIp: "127.0.0.1",
            deviceFingerprint: "fingerprint_abc",
            actionIntervalStdDev: 140.0f,
            isWebdriver: 0.0f
        );

        // Assert
        Assert.Equal(AntiBotDecision.Allow, result.Decision);
        Assert.False(result.IsFailOpen);
        Assert.True(result.ExecutionDurationMs >= 0);
        Assert.Equal(12.0f, Math.Round(result.Vector.TimeToCheckout, 1));
    }
}
