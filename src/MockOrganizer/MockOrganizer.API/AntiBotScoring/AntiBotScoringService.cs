using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MockOrganizer.API.AntiBotScoring;

/// <summary>
/// Kết quả thống kê Benchmark p99 phục vụ kiểm thử SLA US-8.3 (SCRUM-258).
/// </summary>
public record BenchmarkResult
{
    public int Iterations { get; init; }
    public double TotalDurationMs { get; init; }
    public double MeanMs { get; init; }
    public double MinMs { get; init; }
    public double MaxMs { get; init; }
    public double P50Ms { get; init; }
    public double P90Ms { get; init; }
    public double P95Ms { get; init; }
    public double P99Ms { get; init; }
    public double P999Ms { get; init; }
    public double ThroughputRps { get; init; }
    public bool SlaOk { get; init; } // p99 < 2.0ms
}

public interface IAntiBotScoringService
{
    AntiBotScoreResponse Evaluate(BotRiskVector vector);
    BenchmarkResult RunBenchmark(int iterations = 10000);
}

/// <summary>
/// Dịch vụ chấm điểm Bot rủi ro bằng ONNX Runtime nhúng trực tiếp (In-Process Singleton).
/// </summary>
public class AntiBotScoringService : IAntiBotScoringService, IDisposable
{
    private readonly ILogger<AntiBotScoringService> _logger;
    private readonly AntiBotScoringOptions _options;
    private readonly InferenceSession? _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly IAntiBotPolicyManager? _policyManager;
    private bool _disposed;

    public AntiBotScoringService(
        IOptions<AntiBotScoringOptions> options,
        ILogger<AntiBotScoringService> logger,
        IAntiBotPolicyManager? policyManager = null)
    {
        _logger = logger;
        _options = options.Value;
        _policyManager = policyManager;
        _inputName = _options.InputNodeName;
        _outputName = _options.OutputNodeName;

        string resolvedPath = ResolveModelPath(_options.ModelPath);

        if (File.Exists(resolvedPath))
        {
            var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                IntraOpNumThreads = Environment.ProcessorCount,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            _session = new InferenceSession(resolvedPath, sessionOptions);
            _logger.LogInformation("Đã khởi tạo thành công ONNX InferenceSession từ đường dẫn: {Path}", resolvedPath);
        }
        else
        {
            _logger.LogWarning("Không tìm thấy file mô hình ONNX tại: {Path}. Dịch vụ sẽ hoạt động ở chế độ Fallback heuristic.", resolvedPath);
        }
    }

    private static string ResolveModelPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        // 1. Thử tìm trong AppContext.BaseDirectory
        string baseDir = AppContext.BaseDirectory;
        string combined = Path.Combine(baseDir, configuredPath);
        if (File.Exists(combined)) return combined;

        // 2. Thử tìm trong Current Directory
        string currentDir = Directory.GetCurrentDirectory();
        combined = Path.Combine(currentDir, configuredPath);
        if (File.Exists(combined)) return combined;

        // 3. Tìm ngược lên các thư mục cha từ Current Directory và BaseDirectory
        var candidates = new[] { currentDir, baseDir };
        foreach (var startDir in candidates)
        {
            string? dir = startDir;
            for (int i = 0; i < 6 && dir != null; i++)
            {
                var p1 = Path.Combine(dir, configuredPath);
                if (File.Exists(p1)) return p1;

                var p2 = Path.Combine(dir, "src", "MockOrganizer", "MockOrganizer.API", configuredPath);
                if (File.Exists(p2)) return p2;

                var p3 = Path.Combine(dir, "MockOrganizer.API", configuredPath);
                if (File.Exists(p3)) return p3;

                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        return configuredPath;
    }

    public AntiBotScoreResponse Evaluate(BotRiskVector vector)
    {
        var sw = Stopwatch.StartNew();

        float botProbability;

        if (_session != null)
        {
            // Inference qua ONNX Runtime
            float[] features = vector.ToArray();
            var inputTensor = new DenseTensor<float>(features, [1, 6]);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
            };

            using var results = _session.Run(inputs);
            var probTensor = results.First(r => r.Name == _outputName).AsTensor<float>();

            // Output tensor có shape [1, 2]: probTensor[0, 0] = Human, probTensor[0, 1] = Bot
            botProbability = probTensor.GetValue(1);
        }
        else
        {
            // Heuristic Fallback nếu chưa load được model file
            botProbability = ComputeHeuristicScore(vector);
        }

        sw.Stop();
        double elapsedMs = sw.Elapsed.TotalMilliseconds;

        var policy = _policyManager?.GetPolicy();
        float allowThreshold = policy?.AllowThreshold ?? _options.AllowThreshold;
        float blockThreshold = policy?.BlockThreshold ?? _options.BlockThreshold;

        AntiBotDecision decision = botProbability switch
        {
            _ when botProbability < allowThreshold => AntiBotDecision.Allow,
            _ when botProbability >= blockThreshold => AntiBotDecision.Block,
            _ => AntiBotDecision.Challenge
        };

        return new AntiBotScoreResponse
        {
            RiskScore = (float)Math.Round(botProbability, 4),
            Decision = decision.ToString(),
            InferenceTimeMs = Math.Round(elapsedMs, 3),
            Details = $"Evaluated using {(_session != null ? "ONNX Runtime (Embedded)" : "Fallback Heuristic")}"
        };
    }

    public BenchmarkResult RunBenchmark(int iterations = 10000)
    {
        var dummyVectors = new BotRiskVector[]
        {
            new() { TimeToCheckout = 12.5f, ActionIntervalStdDev = 190.0f, BurstRate10s = 1.0f, ConcurrentHold = 0.0f, IsWebDriver = 0.0f, DeviceAccountOverlap = 0.02f },
            new() { TimeToCheckout = 0.8f,  ActionIntervalStdDev = 10.0f,  BurstRate10s = 15.0f, ConcurrentHold = 8.0f, IsWebDriver = 1.0f, DeviceAccountOverlap = 0.95f },
            new() { TimeToCheckout = 4.0f,  ActionIntervalStdDev = 60.0f,  BurstRate10s = 4.0f,  ConcurrentHold = 2.0f, IsWebDriver = 0.0f, DeviceAccountOverlap = 0.50f }
        };

        // 1. Warm-up (500 lượt để JIT và model cache sẵn sàng)
        for (int i = 0; i < 500; i++)
        {
            Evaluate(dummyVectors[i % dummyVectors.Length]);
        }

        // 2. Thực hiện đo đạc
        var latencies = new double[iterations];
        var totalSw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            Evaluate(dummyVectors[i % dummyVectors.Length]);
            sw.Stop();
            latencies[i] = sw.Elapsed.TotalMilliseconds;
        }

        totalSw.Stop();

        // 3. Tính toán các phân vị (Percentiles)
        Array.Sort(latencies);

        double mean = latencies.Average();
        double min = latencies[0];
        double max = latencies[^1];
        double p50 = GetPercentile(latencies, 0.50);
        double p90 = GetPercentile(latencies, 0.90);
        double p95 = GetPercentile(latencies, 0.95);
        double p99 = GetPercentile(latencies, 0.99);
        double p999 = GetPercentile(latencies, 0.999);

        double totalMs = totalSw.Elapsed.TotalMilliseconds;
        double throughput = iterations / (totalMs / 1000.0);

        return new BenchmarkResult
        {
            Iterations = iterations,
            TotalDurationMs = Math.Round(totalMs, 2),
            MeanMs = Math.Round(mean, 4),
            MinMs = Math.Round(min, 4),
            MaxMs = Math.Round(max, 4),
            P50Ms = Math.Round(p50, 4),
            P90Ms = Math.Round(p90, 4),
            P95Ms = Math.Round(p95, 4),
            P99Ms = Math.Round(p99, 4),
            P999Ms = Math.Round(p999, 4),
            ThroughputRps = Math.Round(throughput, 1),
            SlaOk = p99 < 2.0 // SLA cam kết p99 < 2ms
        };
    }

    private static double GetPercentile(double[] sortedArray, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sortedArray.Length) - 1;
        return sortedArray[Math.Clamp(index, 0, sortedArray.Length - 1)];
    }

    private static float ComputeHeuristicScore(BotRiskVector v)
    {
        float score = 0.0f;
        if (v.IsWebDriver > 0.5f) score += 0.50f;
        if (v.ActionIntervalStdDev >= 0.0f && v.ActionIntervalStdDev < 25.0f) score += 0.25f;
        if (v.TimeToCheckout >= 0.0f && v.TimeToCheckout < 2.0f) score += 0.20f;
        if (v.BurstRate10s > 6.0f) score += 0.20f;
        if (v.ConcurrentHold > 4.0f) score += 0.20f;
        if (v.DeviceAccountOverlap > 0.6f) score += 0.20f;
        return Math.Clamp(score, 0.0f, 1.0f);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _session?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
