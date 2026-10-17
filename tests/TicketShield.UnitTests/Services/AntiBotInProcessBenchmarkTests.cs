using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MockOrganizer.API.AntiBotScoring;
using Xunit.Abstractions;

namespace TicketShield.UnitTests.Services;

/// <summary>
/// TEST-8.3.3: In-Process Inference Latency Benchmark Under 10k Invocations
/// Mục tiêu: Kiểm thử 10.000 lượt suy luận liên tục để chứng minh không có độ trễ mạng HTTP microservice (SLA p99 < 2ms trên CPU).
/// Khóa kiểm thử hoàn tất US-8.3.
/// </summary>
public class AntiBotInProcessBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public AntiBotInProcessBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test_8_3_3_InProcess_Inference_Latency_Benchmark_10k_Under_2ms_SLA()
    {
        // 1. Arrange: Nạp mô hình ONNX Singleton
        string modelPath = Path.Combine(AppContext.BaseDirectory, "models", "anti_bot_model.onnx");
        if (!File.Exists(modelPath))
        {
            // Tìm ngược lên nếu chạy từ test runner runner directory
            var current = Directory.GetCurrentDirectory();
            for (int i = 0; i < 5; i++)
            {
                var candidate = Path.Combine(current, "src", "MockOrganizer", "MockOrganizer.API", "models", "anti_bot_model.onnx");
                if (File.Exists(candidate))
                {
                    modelPath = candidate;
                    break;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
        }

        Assert.True(File.Exists(modelPath), $"Không tìm thấy file anti_bot_model.onnx tại {modelPath}");

        var options = Options.Create(new AntiBotScoringOptions
        {
            ModelPath = modelPath,
            AllowThreshold = 0.35f,
            BlockThreshold = 0.75f
        });

        using var service = new AntiBotScoringService(options, NullLogger<AntiBotScoringService>.Instance);

        // 2. Act: Thực thi 10.000 lượt suy luận liên tục
        const int iterations = 10000;
        var benchmark = service.RunBenchmark(iterations);

        // 3. Log chi tiết số liệu phân vị
        _output.WriteLine("=========================================================================");
        _output.WriteLine(" TEST-8.3.3: IN-PROCESS INFERENCE BENCHMARK REPORT (10,000 INVOCATIONS)  ");
        _output.WriteLine("=========================================================================");
        _output.WriteLine($" Model Path          : {modelPath}");
        _output.WriteLine($" Iterations          : {benchmark.Iterations:N0}");
        _output.WriteLine($" Total Duration      : {benchmark.TotalDurationMs:F2} ms");
        _output.WriteLine($" Throughput (RPS)    : {benchmark.ThroughputRps:N1} req/s");
        _output.WriteLine("-------------------------------------------------------------------------");
        _output.WriteLine($" Mean Latency        : {benchmark.MeanMs:F4} ms");
        _output.WriteLine($" Min Latency         : {benchmark.MinMs:F4} ms");
        _output.WriteLine($" Max Latency         : {benchmark.MaxMs:F4} ms");
        _output.WriteLine($" Median (p50) Latency: {benchmark.P50Ms:F4} ms");
        _output.WriteLine($" p90 Latency         : {benchmark.P90Ms:F4} ms");
        _output.WriteLine($" p95 Latency         : {benchmark.P95Ms:F4} ms");
        _output.WriteLine($" p99 Latency         : {benchmark.P99Ms:F4} ms  <-- SLA TARGET (< 2.0 ms)");
        _output.WriteLine($" p99.9 Latency       : {benchmark.P999Ms:F4} ms");
        _output.WriteLine($" SLA Met (p99 < 2ms) : {benchmark.SlaOk}");
        _output.WriteLine("=========================================================================");

        // 4. Assert: Kiểm định các ràng buộc SLA
        Assert.Equal(iterations, benchmark.Iterations);
        Assert.True(benchmark.SlaOk, $"SLA thất bại: p99 latency đạt {benchmark.P99Ms} ms (vượt ngưỡng 2.0 ms)");
        Assert.True(benchmark.P99Ms < 2.0, $"p99 latency ({benchmark.P99Ms} ms) không nhỏ hơn 2.0 ms");
        Assert.True(benchmark.MeanMs < 1.0, $"Mean latency ({benchmark.MeanMs} ms) phải < 1.0 ms đối với In-Process inference");
        Assert.True(benchmark.ThroughputRps > 5000, $"Throughput ({benchmark.ThroughputRps} rps) phải > 5,000 rps");
    }

    [Fact]
    public void Test_8_3_3_Three_Decision_Tiers_Accuracy()
    {
        string modelPath = Path.Combine(AppContext.BaseDirectory, "models", "anti_bot_model.onnx");
        if (!File.Exists(modelPath))
        {
            var current = Directory.GetCurrentDirectory();
            for (int i = 0; i < 5; i++)
            {
                var candidate = Path.Combine(current, "src", "MockOrganizer", "MockOrganizer.API", "models", "anti_bot_model.onnx");
                if (File.Exists(candidate))
                {
                    modelPath = candidate;
                    break;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
        }

        var options = Options.Create(new AntiBotScoringOptions { ModelPath = modelPath });
        using var service = new AntiBotScoringService(options, NullLogger<AntiBotScoringService>.Instance);

        // Test 1: Human User -> Phải Allow (< 0.35)
        var human = new BotRiskVector
        {
            TimeToCheckout = 12.5f,
            ActionIntervalStdDev = 190.0f,
            BurstRate10s = 1.0f,
            ConcurrentHold = 0.0f,
            IsWebDriver = 0.0f,
            DeviceAccountOverlap = 0.02f
        };
        var resHuman = service.Evaluate(human);
        Assert.Equal(AntiBotDecision.Allow.ToString(), resHuman.Decision);
        Assert.True(resHuman.RiskScore < 0.35f);

        // Test 2: Obvious Bot -> Phải Block (>= 0.75)
        var bot = new BotRiskVector
        {
            TimeToCheckout = 0.8f,
            ActionIntervalStdDev = 10.0f,
            BurstRate10s = 15.0f,
            ConcurrentHold = 8.0f,
            IsWebDriver = 1.0f,
            DeviceAccountOverlap = 0.95f
        };
        var resBot = service.Evaluate(bot);
        Assert.Equal(AntiBotDecision.Block.ToString(), resBot.Decision);
        Assert.True(resBot.RiskScore >= 0.75f);

        // Test 3: Grey Zone / Phe vé -> Phải Challenge (0.35 <= Score < 0.75)
        var grey = new BotRiskVector
        {
            TimeToCheckout = 4.0f,
            ActionIntervalStdDev = 55.0f,
            BurstRate10s = 4.0f,
            ConcurrentHold = 3.0f,
            IsWebDriver = 0.0f,
            DeviceAccountOverlap = 0.50f
        };
        var resGrey = service.Evaluate(grey);
        Assert.Equal(AntiBotDecision.Challenge.ToString(), resGrey.Decision);
        Assert.True(resGrey.RiskScore >= 0.35f && resGrey.RiskScore < 0.75f);
    }
}
