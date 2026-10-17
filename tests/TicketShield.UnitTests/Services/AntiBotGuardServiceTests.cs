using Microsoft.Extensions.Logging.Abstractions;
using MockOrganizer.API.AntiBotScoring;
using MockOrganizer.API.Controllers;
using MockOrganizer.API.Models;
using MockOrganizer.API.Services;
using Moq;
using Xunit;

namespace TicketShield.UnitTests.Services;

/// <summary>
/// Unit Tests cho BE-PIPE-8.2.3 (SCRUM-253):
/// 6D Feature Vector Assembly and 20ms Fail-Open Fallback Guard.
/// Kiểm thử tính toán vector 6 chiều chuẩn hóa, định tuyến quyết định 3 tầng,
/// và cơ chế Fail-Open tự động khi Redis / ONNX runtime chậm hơn SLA 20ms hoặc gặp lỗi.
/// </summary>
public sealed class AntiBotGuardServiceTests
{
    private readonly Mock<IServerTransactionTelemetryService> _telemetryMock;
    private readonly Mock<IAntiBotScoringService> _scoringMock;
    private readonly AntiBotGuardService _guardService;

    public AntiBotGuardServiceTests()
    {
        _telemetryMock = new Mock<IServerTransactionTelemetryService>();
        _scoringMock = new Mock<IAntiBotScoringService>();

        _guardService = new AntiBotGuardService(
            _telemetryMock.Object,
            _scoringMock.Object,
            NullLogger<AntiBotGuardService>.Instance);
    }

    [Fact]
    public async Task EvaluateCheckoutGuard_WhenHumanUser_ReturnsAllowWithLowRiskScore()
    {
        // 1. Arrange
        var request = new CheckoutRequest
        {
            TierId = Guid.NewGuid(),
            Quantity = 2,
            BuyerEmail = "human@ticketshield.vn",
            SessionToken = "valid.session.token",
            Telemetry = new ClientTelemetryDto
            {
                IsWebdriver = 0,
                ActionIntervalStdDev = 190.5f,
                DeviceFingerprint = "dev-human-1"
            }
        };

        var telemetryResult = new ServerTransactionTelemetryResult(
            TimeToCheckoutSeconds: 12.5,
            ConcurrentHoldCount: 0,
            IssuedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-12.5),
            ReceivedAtUtc: DateTimeOffset.UtcNow,
            ExecutionDurationMs: 0.4,
            IsSessionValid: true,
            BurstRate10s: 1,
            DeviceAccountOverlap: 0,
            RedisDurationMs: 0.2);

        _telemetryMock
            .Setup(t => t.MeasureAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(telemetryResult);

        _scoringMock
            .Setup(s => s.Evaluate(It.IsAny<BotRiskVector>()))
            .Returns(new AntiBotScoreResponse
            {
                Decision = AntiBotDecision.Allow.ToString(),
                RiskScore = 0.02f,
                InferenceTimeMs = 0.3,
                Details = "Human profile"
            });

        // 2. Act
        var result = await _guardService.EvaluateCheckoutGuardAsync(request, "192.168.1.100");

        // 3. Assert
        Assert.Equal(AntiBotDecision.Allow, result.Decision);
        Assert.True(result.RiskScore < 0.35f);
        Assert.False(result.IsFailOpen);
        Assert.NotNull(result.Vector);
        Assert.Equal(12.5f, result.Vector.TimeToCheckout);
        Assert.Equal(190.5f, result.Vector.ActionIntervalStdDev);
        Assert.Equal(1.0f, result.Vector.BurstRate10s);
        Assert.Equal(0.0f, result.Vector.ConcurrentHold);
        Assert.Equal(0.0f, result.Vector.IsWebDriver);
        Assert.Equal(0.0f, result.Vector.DeviceAccountOverlap);
    }

    [Fact]
    public async Task EvaluateCheckoutGuard_WhenObviousBot_ReturnsBlockWithHighRiskScore()
    {
        // 1. Arrange: Tốc độ máy tính, lộ cờ webdriver, burst cao, concurrent hold lớn
        var request = new CheckoutRequest
        {
            TierId = Guid.NewGuid(),
            Quantity = 4,
            BuyerEmail = "bot@scalper.net",
            SessionToken = "valid.session.token",
            Telemetry = new ClientTelemetryDto
            {
                IsWebdriver = 1,
                ActionIntervalStdDev = 8.5f,
                DeviceFingerprint = "dev-bot-farm"
            }
        };

        var telemetryResult = new ServerTransactionTelemetryResult(
            TimeToCheckoutSeconds: 0.6,
            ConcurrentHoldCount: 8,
            IssuedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-0.6),
            ReceivedAtUtc: DateTimeOffset.UtcNow,
            ExecutionDurationMs: 0.5,
            IsSessionValid: true,
            BurstRate10s: 18,
            DeviceAccountOverlap: 1,
            RedisDurationMs: 0.3);

        _telemetryMock
            .Setup(t => t.MeasureAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(telemetryResult);

        _scoringMock
            .Setup(s => s.Evaluate(It.IsAny<BotRiskVector>()))
            .Returns(new AntiBotScoreResponse
            {
                Decision = AntiBotDecision.Block.ToString(),
                RiskScore = 0.98f,
                InferenceTimeMs = 0.4,
                Details = "Bot detected"
            });

        // 2. Act
        var result = await _guardService.EvaluateCheckoutGuardAsync(request, "45.76.12.3");

        // 3. Assert
        Assert.Equal(AntiBotDecision.Block, result.Decision);
        Assert.True(result.RiskScore >= 0.75f);
        Assert.False(result.IsFailOpen);
        Assert.Equal(1.0f, result.Vector.IsWebDriver);
        Assert.Equal(18.0f, result.Vector.BurstRate10s);
        Assert.Equal(8.0f, result.Vector.ConcurrentHold);
    }

    [Fact]
    public async Task EvaluateCheckoutGuard_WhenStealthBot_ReturnsChallenge()
    {
        // 1. Arrange: Vùng xám (Grey Zone)
        var request = new CheckoutRequest
        {
            TierId = Guid.NewGuid(),
            Quantity = 2,
            BuyerEmail = "stealth@gmail.com",
            SessionToken = "valid.session.token",
            Telemetry = new ClientTelemetryDto
            {
                IsWebdriver = 0,
                ActionIntervalStdDev = 55.0f,
                DeviceFingerprint = "dev-grey"
            }
        };

        var telemetryResult = new ServerTransactionTelemetryResult(
            TimeToCheckoutSeconds: 4.0,
            ConcurrentHoldCount: 3,
            IssuedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-4.0),
            ReceivedAtUtc: DateTimeOffset.UtcNow,
            ExecutionDurationMs: 0.4,
            IsSessionValid: true,
            BurstRate10s: 4,
            DeviceAccountOverlap: 1,
            RedisDurationMs: 0.2);

        _telemetryMock
            .Setup(t => t.MeasureAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(telemetryResult);

        _scoringMock
            .Setup(s => s.Evaluate(It.IsAny<BotRiskVector>()))
            .Returns(new AntiBotScoreResponse
            {
                Decision = AntiBotDecision.Challenge.ToString(),
                RiskScore = 0.55f,
                InferenceTimeMs = 0.3,
                Details = "Verification required"
            });

        // 2. Act
        var result = await _guardService.EvaluateCheckoutGuardAsync(request, "118.69.1.5");

        // 3. Assert
        Assert.Equal(AntiBotDecision.Challenge, result.Decision);
        Assert.True(result.RiskScore >= 0.35f && result.RiskScore < 0.75f);
        Assert.False(result.IsFailOpen);
    }

    [Fact]
    public async Task EvaluateCheckoutGuard_WhenOperationExceeds20msTimeout_TriggersFailOpenAllow()
    {
        // 1. Arrange: Giả lập Redis hoặc IO bị trễ 50ms (> 20ms SLA)
        var request = new CheckoutRequest
        {
            TierId = Guid.NewGuid(),
            Quantity = 1,
            BuyerEmail = "slow@ticketshield.vn"
        };

        _telemetryMock
            .Setup(t => t.MeasureAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (string? a, string? b, string? c, string? d, CancellationToken ct) =>
            {
                await Task.Delay(50, ct);
                return new ServerTransactionTelemetryResult(
                    TimeToCheckoutSeconds: 5.0,
                    ConcurrentHoldCount: 0,
                    IssuedAtUtc: DateTimeOffset.UtcNow,
                    ReceivedAtUtc: DateTimeOffset.UtcNow,
                    ExecutionDurationMs: 50.0,
                    IsSessionValid: true);
            });

        // 2. Act
        var result = await _guardService.EvaluateCheckoutGuardAsync(request, "127.0.0.1");

        // 3. Assert: Cơ chế 20ms Fail-Open bảo vệ người dùng, cho phép thanh toán (Allow), điểm 0.0f
        Assert.Equal(AntiBotDecision.Allow, result.Decision);
        Assert.Equal(0.0f, result.RiskScore);
        Assert.True(result.IsFailOpen);
        Assert.Contains("Fail-Open", result.Reason);
    }

    [Fact]
    public async Task EvaluateCheckoutGuard_WhenTelemetryThrowsException_TriggersFailOpenAllow()
    {
        // 1. Arrange: Giả lập lỗi kết nối Redis hoặc ngoại lệ không mong muốn
        var request = new CheckoutRequest
        {
            TierId = Guid.NewGuid(),
            Quantity = 1,
            BuyerEmail = "error@ticketshield.vn"
        };

        _telemetryMock
            .Setup(t => t.MeasureAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis cluster connection severed"));

        // 2. Act
        var result = await _guardService.EvaluateCheckoutGuardAsync(request, "127.0.0.1");

        // 3. Assert: Không làm crash hoặc gián đoạn luồng thanh toán, tự động Fail-Open
        Assert.Equal(AntiBotDecision.Allow, result.Decision);
        Assert.Equal(0.0f, result.RiskScore);
        Assert.True(result.IsFailOpen);
        Assert.Contains("Fail-Open", result.Reason);
    }

    [Fact]
    public async Task EvaluateCheckoutGuard_WhenClientTelemetryMissing_MapsToNegativeOnePerSpec08()
    {
        // 1. Arrange: Client không gửi object telemetry (ví dụ: bot gọi API trực tiếp)
        var request = new CheckoutRequest
        {
            TierId = Guid.NewGuid(),
            Quantity = 1,
            BuyerEmail = "notelenetry@ticketshield.vn",
            SessionToken = null,
            Telemetry = null // Missing client telemetry
        };

        var telemetryResult = new ServerTransactionTelemetryResult(
            TimeToCheckoutSeconds: -1.0,
            ConcurrentHoldCount: 0,
            IssuedAtUtc: DateTimeOffset.MinValue,
            ReceivedAtUtc: DateTimeOffset.UtcNow,
            ExecutionDurationMs: 0.3,
            IsSessionValid: false,
            BurstRate10s: 1,
            DeviceAccountOverlap: -1);

        _telemetryMock
            .Setup(t => t.MeasureAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(telemetryResult);

        BotRiskVector? capturedVector = null;
        _scoringMock
            .Setup(s => s.Evaluate(It.IsAny<BotRiskVector>()))
            .Callback<BotRiskVector>(v => capturedVector = v)
            .Returns(new AntiBotScoreResponse
            {
                Decision = AntiBotDecision.Block.ToString(),
                RiskScore = 0.88f
            });

        // 2. Act
        var result = await _guardService.EvaluateCheckoutGuardAsync(request, "10.0.0.1");

        // 3. Assert: Các trường thiếu phải được gán -1.0f theo Spec 08 (Không tự bịa ra giá trị giống người)
        Assert.NotNull(capturedVector);
        Assert.Equal(-1.0f, capturedVector.TimeToCheckout);
        Assert.Equal(-1.0f, capturedVector.ActionIntervalStdDev);
        Assert.Equal(-1.0f, capturedVector.IsWebDriver);
        Assert.Equal(-1.0f, capturedVector.DeviceAccountOverlap);
    }

    [Fact]
    public void BotRiskVector_ToArray_MatchesStandard6DOrder()
    {
        var vector = new BotRiskVector
        {
            TimeToCheckout = 12.3f,
            ActionIntervalStdDev = 145.6f,
            BurstRate10s = 2.0f,
            ConcurrentHold = 1.0f,
            IsWebDriver = 0.0f,
            DeviceAccountOverlap = 0.05f
        };

        var array = vector.ToArray();

        Assert.Equal(6, array.Length);
        Assert.Equal(12.3f, array[0]);   // [0] time_to_checkout
        Assert.Equal(145.6f, array[1]);  // [1] action_interval_std_dev
        Assert.Equal(2.0f, array[2]);    // [2] burst_rate_10s
        Assert.Equal(1.0f, array[3]);    // [3] concurrent_hold
        Assert.Equal(0.0f, array[4]);    // [4] is_webdriver
        Assert.Equal(0.05f, array[5]);   // [5] device_account_overlap
    }
}
