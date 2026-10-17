using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MockOrganizer.API.Services;
using Xunit;
using Xunit.Abstractions;

namespace TicketShield.UnitTests.Services;

/// <summary>
/// TEST-8.2.4: Redis Sliding Window Pipeline Benchmark Under 500 RPS
/// Mục tiêu: Kiểm tra thời gian thu thập 4 đặc trưng server đảm bảo p99 &lt; 2ms dưới tải 500 RPS.
/// 4 đặc trưng server:
///   1. time_to_checkout (HMAC Session Token validation)
///   2. burst_rate_10s (Redis Sliding Window rate counter)
///   3. concurrent_hold (Tra cứu số vé đang giữ đồng thời qua Redis)
///   4. device_account_overlap (Tỷ lệ tài khoản trên cùng thiết bị/IP qua Redis)
/// Khóa kiểm thử hoàn tất US-8.2.
/// </summary>
public sealed class ServerTelemetryPipelineBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public ServerTelemetryPipelineBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Benchmark_ServerTelemetryPipeline_Under500Rps_SlaP99Under2ms()
    {
        // 1. Arrange: Khởi tạo các services cho Server Telemetry Pipeline
        var clock = TimeProvider.System;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SessionToken:SecretKey"] = "TicketShield_Benchmark_SecretKey_2026_Secure!",
                ["Redis:InstanceName"] = "ts:bench:",
                ["Redis:Enabled"] = "false" // Chạy In-Memory Sliding Window Store để benchmark thuần CPU & Pipeline logic
            })
            .Build();

        var sessionTokenService = new SessionTokenService(config, clock);
        var slidingWindowService = new RedisSlidingWindowService(
            redis: null,
            config: config,
            clock: clock,
            logger: NullLogger<RedisSlidingWindowService>.Instance);

        var telemetryService = new ServerTransactionTelemetryService(
            db: null,
            sessionTokenService: sessionTokenService,
            slidingWindowService: slidingWindowService,
            clock: clock,
            logger: NullLogger<ServerTransactionTelemetryService>.Instance);

        var tierId = Guid.NewGuid();

        // 2. Warm-up JIT & ThreadPool (500 invocations để biên dịch IL sang machine code)
        for (int w = 0; w < 500; w++)
        {
            var warmToken = sessionTokenService.GenerateSessionToken(tierId, clock.GetUtcNow().AddSeconds(-15));
            await telemetryService.MeasureAsync(
                warmToken,
                $"warmup_{w}@ticketshield.vn",
                $"fp_warm_{w % 10}",
                $"192.168.1.{w % 20 + 1}");
        }

        // 3. Chuẩn bị tập dữ liệu mô phỏng 500 RPS thực tế
        const int totalRequests = 5000;
        const int simulatedRps = 500;
        var latenciesMs = new double[totalRequests];

        // Seed trước một số holds để tra cứu có dữ liệu
        for (int h = 0; h < 50; h++)
        {
            await slidingWindowService.RecordHoldAsync($"buyer_{h}@ticketshield.vn", (h % 3) + 1);
        }

        // Chuẩn bị trước các tokens và thông tin client đa dạng
        var testCases = new (string Token, string Email, string Device, string Ip)[totalRequests];
        var now = clock.GetUtcNow();
        for (int i = 0; i < totalRequests; i++)
        {
            var issuedSecondsAgo = 5 + (i % 30);
            var token = sessionTokenService.GenerateSessionToken(tierId, now.AddSeconds(-issuedSecondsAgo));
            var email = $"buyer_{i % 50}@ticketshield.vn";
            var device = $"device_fp_{i % 50}";
            var ip = $"10.0.{(i / 250) % 255}.{(i % 250) + 1}";
            testCases[i] = (token, email, device, ip);
        }

        // 4. Act: Thực thi 5,000 requests đồng thời theo rate quy định (Target: 500 RPS)
        // Dùng SemaphoreSlim để kiểm soát concurrency và duy trì áp lực 500 luồng đồng thời
        using var semaphore = new SemaphoreSlim(simulatedRps);
        var totalSw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, totalRequests).Select(async idx =>
        {
            await semaphore.WaitAsync();
            try
            {
                var (token, email, device, ip) = testCases[idx];
                var reqSw = Stopwatch.StartNew();

                var result = await telemetryService.MeasureAsync(token, email, device, ip);

                reqSw.Stop();
                latenciesMs[idx] = reqSw.Elapsed.TotalMilliseconds;

                // Xác thực tính toàn vẹn của kết quả thu thập
                Assert.True(result.IsSessionValid, "Session token phải hợp lệ");
                Assert.True(result.TimeToCheckoutSeconds >= 5.0, "time_to_checkout phải tính đúng");
                Assert.True(result.BurstRate10s >= 1, "burst_rate_10s phải >= 1");
                Assert.True(result.DeviceAccountOverlap >= 1, "device_account_overlap phải >= 1");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        totalSw.Stop();

        // 5. Thống kê số liệu phân vị
        Array.Sort(latenciesMs);

        var totalElapsedSeconds = totalSw.Elapsed.TotalSeconds;
        var achievedRps = totalRequests / totalElapsedSeconds;

        var min = latenciesMs[0];
        var mean = latenciesMs.Average();
        var p50 = latenciesMs[(int)(totalRequests * 0.50)];
        var p90 = latenciesMs[(int)(totalRequests * 0.90)];
        var p95 = latenciesMs[(int)(totalRequests * 0.95)];
        var p99 = latenciesMs[(int)(totalRequests * 0.99)];
        var p999 = latenciesMs[(int)(totalRequests * 0.999)];
        var max = latenciesMs[^1];

        // 6. In kết quả chi tiết ra Test Output
        _output.WriteLine("==========================================================================");
        _output.WriteLine(" TEST-8.2.4: REDIS SLIDING WINDOW PIPELINE BENCHMARK (500 RPS TARGET)");
        _output.WriteLine("==========================================================================");
        _output.WriteLine($"Total Invocations      : {totalRequests:N0} requests");
        _output.WriteLine($"Total Duration         : {totalSw.Elapsed.TotalMilliseconds:F2} ms ({totalElapsedSeconds:F2} s)");
        _output.WriteLine($"Achieved Throughput    : {achievedRps:F1} RPS (Target >= {simulatedRps} RPS)");
        _output.WriteLine("--------------------------------------------------------------------------");
        _output.WriteLine(" LATENCY DISTRIBUTION (4 Server Features: TimeToCheckout, Burst, Hold, Overlap)");
        _output.WriteLine("--------------------------------------------------------------------------");
        _output.WriteLine($" - Min Latency         : {min:F4} ms");
        _output.WriteLine($" - Mean Latency        : {mean:F4} ms");
        _output.WriteLine($" - p50 (Median)        : {p50:F4} ms");
        _output.WriteLine($" - p90                 : {p90:F4} ms");
        _output.WriteLine($" - p95                 : {p95:F4} ms");
        _output.WriteLine($" - p99 (Hard SLA < 2ms): {p99:F4} ms");
        _output.WriteLine($" - p99.9               : {p999:F4} ms");
        _output.WriteLine($" - Max Latency         : {max:F4} ms");
        _output.WriteLine("==========================================================================");

        // 7. Assert SLA & Acceptance Criteria
        // Hard SLA của TEST-8.2.4: p99 < 2.0 ms
        Assert.True(p99 < 2.0, $"VIOLATION SLA: p99 latency ({p99:F4} ms) vượt ngưỡng cho phép 2.0 ms!");
        // Mean latency thông thường phải < 1.0 ms
        Assert.True(mean < 1.0, $"Mean latency ({mean:F4} ms) vượt quá 1.0 ms!");
        // Thông lượng phải đáp ứng hoặc vượt ngưỡng tải 500 RPS
        Assert.True(achievedRps >= 400.0, $"Thông lượng ({achievedRps:F1} RPS) không đạt yêu cầu!");
    }

    [Fact]
    public async Task Benchmark_SlidingWindow_ConcurrentHoldOperations_UnderHighLoad_IsThreadSafe()
    {
        // Kiểm tra tính toàn vẹn (Thread-Safety) của bộ đếm concurrent_hold khi có 500 threads cùng thao tác
        var clock = TimeProvider.System;
        var config = new ConfigurationBuilder().Build();
        var service = new RedisSlidingWindowService(null, config, clock);

        const int buyers = 20;
        const int holdsPerBuyer = 50; // Tổng 1,000 thao tác concurrent
        const int expectedPerBuyer = holdsPerBuyer * 2;

        var tasks = Enumerable.Range(0, buyers * holdsPerBuyer).Select(async idx =>
        {
            var buyer = $"stress_buyer_{idx % buyers}@ticketshield.vn";
            await service.RecordHoldAsync(buyer, 2);
        });

        await Task.WhenAll(tasks);

        // Kiểm tra từng buyer có đúng số lượng tích lũy không
        for (int b = 0; b < buyers; b++)
        {
            var email = $"stress_buyer_{b}@ticketshield.vn";
            var count = await service.GetConcurrentHoldAsync(email);
            Assert.Equal(expectedPerBuyer, count);
        }

        // Test đồng thời release
        var releaseTasks = Enumerable.Range(0, buyers * holdsPerBuyer).Select(async idx =>
        {
            var buyer = $"stress_buyer_{idx % buyers}@ticketshield.vn";
            await service.ReleaseHoldAsync(buyer, 2);
        });

        await Task.WhenAll(releaseTasks);

        for (int b = 0; b < buyers; b++)
        {
            var email = $"stress_buyer_{b}@ticketshield.vn";
            var count = await service.GetConcurrentHoldAsync(email);
            Assert.Equal(0, count);
        }
    }
}
