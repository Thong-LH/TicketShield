using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using MockOrganizer.API.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

public sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private long _ticks = initial.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
}

/// <summary>
/// Unit Tests & Benchmark Tests cho BE-PIPE-8.2.2 (SCRUM-252):
/// Frequency Rate Counter (burst_rate_10s) và Device Overlap (device_account_overlap)
/// qua Redis Sliding Window với SLA &lt; 1.5ms.
/// </summary>
public sealed class RedisSlidingWindowServiceTests
{
    private readonly MutableTimeProvider _timeProvider;
    private readonly IConfiguration _config;
    private readonly RedisSlidingWindowService _service;

    public RedisSlidingWindowServiceTests()
    {
        _timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero));
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:InstanceName"] = "test:ts:",
                ["Redis:Enabled"] = "false" // Chạy qua In-Memory Sliding Window Store để test độc lập, siêu tốc
            })
            .Build();

        _service = new RedisSlidingWindowService(
            redis: null,
            config: _config,
            clock: _timeProvider);
    }

    // =========================================================================
    // 1. burst_rate_10s Tests (Cửa sổ trượt 10 giây)
    // =========================================================================

    [Fact]
    public async Task RecordAndGetBurstRate_WhenCalledOnce_ReturnsOne()
    {
        var rate = await _service.RecordAndGetBurstRateAsync("192.168.1.10", "fp-dev-1", "user@test.vn");

        Assert.Equal(1, rate);
    }

    [Fact]
    public async Task RecordAndGetBurstRate_WhenSameIpBurstFiveTimesWithinWindow_ReturnsFive()
    {
        const string ip = "10.0.0.50";

        for (int i = 1; i <= 5; i++)
        {
            var rate = await _service.RecordAndGetBurstRateAsync(ip, null, null);
            Assert.Equal(i, rate);
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task RecordAndGetBurstRate_WhenGuestUser_TakesMaxOfIpAndDeviceFingerprint()
    {
        const string ip = "192.168.1.99";
        const string device = "fp-guest-mobile";

        // Giả lập: IP gửi 2 request trước
        await _service.RecordAndGetBurstRateAsync(ip, null, null);
        await _service.RecordAndGetBurstRateAsync(ip, null, null);

        // Device gửi 4 request khác (chẳng hạn xoay proxy qua 4 IP khác nhau nhưng chung 1 device)
        await _service.RecordAndGetBurstRateAsync("10.0.0.1", device, null);
        await _service.RecordAndGetBurstRateAsync("10.0.0.2", device, null);
        await _service.RecordAndGetBurstRateAsync("10.0.0.3", device, null);
        var finalRate = await _service.RecordAndGetBurstRateAsync("10.0.0.4", device, null);

        // Guest mode không có email -> Max(IP rate = 1, Device rate = 4) -> 4
        Assert.Equal(4, finalRate);
    }

    [Fact]
    public async Task RecordAndGetBurstRate_WhenAccountProvided_TakesMaxOfIpDeviceAndAccount()
    {
        const string ip = "172.16.0.5";
        const string device = "fp-desktop-bot";
        const string account = "attacker@scalper.com";

        // IP có 2 lượt
        await _service.RecordAndGetBurstRateAsync(ip, null, null);
        await _service.RecordAndGetBurstRateAsync(ip, null, null);

        // Device có 3 lượt
        await _service.RecordAndGetBurstRateAsync(null, device, null);
        await _service.RecordAndGetBurstRateAsync(null, device, null);
        await _service.RecordAndGetBurstRateAsync(null, device, null);

        // Account đã thực hiện 5 lượt từ nhiều thiết bị/proxy khác nhau
        await _service.RecordAndGetBurstRateAsync(null, null, account);
        await _service.RecordAndGetBurstRateAsync(null, null, account);
        await _service.RecordAndGetBurstRateAsync(null, null, account);
        await _service.RecordAndGetBurstRateAsync(null, null, account);
        await _service.RecordAndGetBurstRateAsync(null, null, account);

        // Khi request tới mang cả IP (lượt 3), Device (lượt 4), Account (lượt 6)
        var result = await _service.RecordAndGetBurstRateAsync(ip, device, account);

        // Max(IP=3, Device=4, Account=6) -> 6
        Assert.Equal(6, result);
    }

    [Fact]
    public async Task RecordAndGetBurstRate_WhenTimeSlidesPastTenSeconds_DiscardsOldRequests()
    {
        const string ip = "192.168.1.200";

        // Gửi 5 requests tại T=0
        for (int i = 0; i < 5; i++)
        {
            await _service.RecordAndGetBurstRateAsync(ip, null, null);
        }

        // Tua đồng hồ thêm 11 giây (> 10s window)
        _timeProvider.Advance(TimeSpan.FromSeconds(11));

        // Request tiếp theo tại T=11s
        var rateAfterSlide = await _service.RecordAndGetBurstRateAsync(ip, null, null);

        // Các request cũ hơn 10s trước đã bị dọn sạch khỏi sliding window, chỉ còn request hiện tại
        Assert.Equal(1, rateAfterSlide);
    }

    [Fact]
    public async Task RecordAndGetBurstRate_WhenNoIdentifiersProvided_ReturnsOne()
    {
        var rate = await _service.RecordAndGetBurstRateAsync(null, null, null);
        Assert.Equal(1, rate);

        var whitespaceRate = await _service.RecordAndGetBurstRateAsync("   ", "   ", "   ");
        Assert.Equal(1, whitespaceRate);
    }

    // =========================================================================
    // 2. device_account_overlap Tests (Cửa sổ trượt 24 giờ, Distinct Accounts)
    // =========================================================================

    [Fact]
    public async Task RecordAndGetDeviceAccountOverlap_WhenDeviceMissing_ReturnsNegativeOne()
    {
        var result1 = await _service.RecordAndGetDeviceAccountOverlapAsync(null, "user@test.vn");
        var result2 = await _service.RecordAndGetDeviceAccountOverlapAsync("   ", "user@test.vn");

        Assert.Equal(-1, result1);
        Assert.Equal(-1, result2);
    }

    [Fact]
    public async Task RecordAndGetDeviceAccountOverlap_WhenSingleAccount_ReturnsOne()
    {
        const string device = "dev-macbook-pro-m3";
        const string email = "user1@ticketshield.vn";

        var count = await _service.RecordAndGetDeviceAccountOverlapAsync(device, email);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RecordAndGetDeviceAccountOverlap_WhenSameAccountRequestsMultipleTimes_DoesNotDoubleCount()
    {
        const string device = "dev-iphone-15";
        const string email = "same_buyer@ticketshield.vn";

        for (int i = 0; i < 10; i++)
        {
            var count = await _service.RecordAndGetDeviceAccountOverlapAsync(device, email);
            Assert.Equal(1, count);
            _timeProvider.Advance(TimeSpan.FromMinutes(5));
        }
    }

    [Fact]
    public async Task RecordAndGetDeviceAccountOverlap_WhenMultipleAccountsOnSameDevice_CountsUniqueAccounts()
    {
        const string device = "dev-shared-lab-pc";

        await _service.RecordAndGetDeviceAccountOverlapAsync(device, "buyer1@ts.vn");
        await _service.RecordAndGetDeviceAccountOverlapAsync(device, "buyer2@ts.vn");
        await _service.RecordAndGetDeviceAccountOverlapAsync(device, "buyer3@ts.vn");
        var count = await _service.RecordAndGetDeviceAccountOverlapAsync(device, "buyer4@ts.vn");

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task RecordAndGetDeviceAccountOverlap_WhenAccountExceeds24Hours_ExpiresAndSlidesOut()
    {
        const string device = "dev-office-pc";

        // Tài khoản 1 thao tác tại T = 0
        await _service.RecordAndGetDeviceAccountOverlapAsync(device, "user_yesterday@ts.vn");

        // Tua thời gian trôi qua 25 tiếng (> 24h window)
        _timeProvider.Advance(TimeSpan.FromHours(25));

        // Tài khoản 2 thao tác tại T = 25h
        var count = await _service.RecordAndGetDeviceAccountOverlapAsync(device, "user_today@ts.vn");

        // user_yesterday đã quá 24h nên bị loại khỏi cửa sổ, chỉ còn user_today
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RecordAndGetDeviceAccountOverlap_WhenAccountRefreshedWithin24Hours_KeepsActive()
    {
        const string device = "dev-cafe-tablet";
        const string accountA = "user_a@ts.vn";
        const string accountB = "user_b@ts.vn";

        // Account A thao tác tại T = 0
        await _service.RecordAndGetDeviceAccountOverlapAsync(device, accountA);

        // Sau 20 tiếng, Account A thao tác lại (làm mới score trong sliding window)
        _timeProvider.Advance(TimeSpan.FromHours(20));
        await _service.RecordAndGetDeviceAccountOverlapAsync(device, accountA);

        // Sau tiếp 10 tiếng nữa (Tổng là 30 tiếng từ T=0, nhưng mới 10 tiếng từ lần thao tác gần nhất của A)
        _timeProvider.Advance(TimeSpan.FromHours(10));

        // Account B thao tác lần đầu
        var count = await _service.RecordAndGetDeviceAccountOverlapAsync(device, accountB);

        // Cả Account A và Account B đều còn trong cửa sổ 24h -> Trả về 2
        Assert.Equal(2, count);
    }

    // =========================================================================
    // 3. Combined Telemetry & Performance SLA Benchmark (< 1.5ms)
    // =========================================================================

    [Fact]
    public async Task MeasureSlidingWindowTelemetry_ReturnsCombinedResultAndMeasuresDuration()
    {
        var result = await _service.MeasureSlidingWindowTelemetryAsync(
            "192.168.1.1", "dev-client-hash-123", "user@ticketshield.vn");

        Assert.Equal(1, result.BurstRate10s);
        Assert.Equal(1, result.DeviceAccountOverlap);
        Assert.True(result.ExecutionDurationMs >= 0.0);
        Assert.False(result.IsRedisAvailable); // Mock in-memory mode
    }

    [Fact]
    public async Task Benchmark_SlidingWindowOperations_ExecuteUnderOnePointFiveMillisecondsSla()
    {
        const int iterations = 1000;
        const string device = "dev-benchmark-device";
        const string ip = "127.0.0.1";
        const string email = "bench@ticketshield.vn";

        // 1. Warm-up
        for (int i = 0; i < 50; i++)
        {
            await _service.MeasureSlidingWindowTelemetryAsync(ip, device, email);
        }

        // 2. Measure 1000 operations
        var latencies = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await _service.MeasureSlidingWindowTelemetryAsync(ip, device, $"bench_{i % 5}@ticketshield.vn");
            sw.Stop();
            latencies[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(latencies);
        var mean = latencies.Average();
        var p99 = latencies[(int)(iterations * 0.99)];

        // SLA cam kết trong ticket: < 1.5ms
        Assert.True(mean < 1.5, $"Mean latency {mean}ms exceeded SLA 1.5ms");
        Assert.True(p99 < 5.0, $"P99 latency {p99}ms exceeded threshold");
    }

    // =========================================================================
    // 4. concurrent_hold Tests (Server Redis Hold Tracking)
    // =========================================================================

    [Fact]
    public async Task GetConcurrentHold_WhenNoHoldRecorded_ReturnsZero()
    {
        var count = await _service.GetConcurrentHoldAsync("buyer@ticketshield.vn");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task RecordHold_WhenRecorded_ReturnsCorrectQuantity()
    {
        const string email = "buyer1@ticketshield.vn";
        await _service.RecordHoldAsync(email, 2);

        var count = await _service.GetConcurrentHoldAsync(email);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RecordHold_WhenMultipleCalls_AccumulatesQuantity()
    {
        const string email = "buyer2@ticketshield.vn";
        await _service.RecordHoldAsync(email, 2);
        await _service.RecordHoldAsync(email, 3);

        var count = await _service.GetConcurrentHoldAsync(email);
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task GetConcurrentHold_WhenHoldExpires_ReturnsZero()
    {
        const string email = "buyer3@ticketshield.vn";
        await _service.RecordHoldAsync(email, 4, TimeSpan.FromMinutes(10));

        // Chưa hết hạn
        _timeProvider.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(4, await _service.GetConcurrentHoldAsync(email));

        // Đã hết hạn (sau 10 phút)
        _timeProvider.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(0, await _service.GetConcurrentHoldAsync(email));
    }

    [Fact]
    public async Task ReleaseHold_DecrementsQuantityCorrectly()
    {
        const string email = "buyer4@ticketshield.vn";
        await _service.RecordHoldAsync(email, 5);
        await _service.ReleaseHoldAsync(email, 2);

        var count = await _service.GetConcurrentHoldAsync(email);
        Assert.Equal(3, count);

        // Giải phóng hết hoặc quá số lượng -> trả về 0
        await _service.ReleaseHoldAsync(email, 5);
        Assert.Equal(0, await _service.GetConcurrentHoldAsync(email));
    }
}
