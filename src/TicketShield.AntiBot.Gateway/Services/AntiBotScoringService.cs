using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TicketShield.AntiBot.Gateway.Models;

namespace TicketShield.AntiBot.Gateway.Services;

public interface IAntiBotScoringService
{
    AntiBotScoreResponse Evaluate(BotRiskVector vector);
    AntiBotScoreResponse EvaluateV2(BotRiskVectorV2 vector);
    ShadowEvaluationResult EvaluateShadow(BotRiskVector v1, BotRiskVectorV2 v2);
    bool IsModelV2Ready { get; }
    string? ModelV2Sha256 { get; }
}

public class AntiBotScoringService : IAntiBotScoringService, IDisposable
{
    private readonly ILogger<AntiBotScoringService> _logger;
    private readonly AntiBotScoringOptions _options;
    private readonly InferenceSession? _session;
    private readonly InferenceSession? _sessionV2;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly string _inputNameV2;
    private readonly string _outputNameV2;
    private readonly string? _modelV2Sha256;
    private bool _disposed;

    public bool IsModelV2Ready => _sessionV2 != null;
    public string? ModelV2Sha256 => _modelV2Sha256;

    public AntiBotScoringService(
        IOptions<AntiBotScoringOptions> options,
        ILogger<AntiBotScoringService> logger)
    {
        _logger = logger;
        _options = options.Value;
        _inputName = _options.InputNodeName;
        _outputName = _options.OutputNodeName;
        _inputNameV2 = _options.ModelV2InputNodeName;
        _outputNameV2 = _options.ModelV2OutputNodeName;

        var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            IntraOpNumThreads = Environment.ProcessorCount,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        // 1. Khởi tạo Model v1.0 (Baseline 6D)
        string resolvedPathV1 = ResolveModelPath(_options.ModelPath);
        if (File.Exists(resolvedPathV1))
        {
            _session = new InferenceSession(resolvedPathV1, sessionOptions);
            _logger.LogInformation("AntiBot Gateway: Initialized ONNX Model v1.0 from: {Path}", resolvedPathV1);
        }
        else
        {
            _logger.LogWarning("AntiBot Gateway: Model v1.0 file not found at: {Path}. Running in Fallback Heuristic mode.", resolvedPathV1);
        }

        // 2. Khởi tạo Model v2.0 (3-Tier 11D - Epic US-13.3)
        string resolvedPathV2 = ResolveModelPath(_options.ModelV2Path);
        if (File.Exists(resolvedPathV2))
        {
            try
            {
                _modelV2Sha256 = ComputeFileSha256(resolvedPathV2);
                if (!string.IsNullOrEmpty(_options.ExpectedV2Sha256) &&
                    !string.Equals(_modelV2Sha256, _options.ExpectedV2Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "AntiBot Gateway: Model v2.0 SHA-256 hash mismatch! Computed: {Computed}, Expected: {Expected}",
                        _modelV2Sha256, _options.ExpectedV2Sha256);
                }
                else
                {
                    _logger.LogInformation("AntiBot Gateway: Model v2.0 SHA-256 verified successfully: {Hash}", _modelV2Sha256);
                }

                _sessionV2 = new InferenceSession(resolvedPathV2, sessionOptions);
                _logger.LogInformation("AntiBot Gateway: Initialized ONNX Model v2.0 from: {Path}", resolvedPathV2);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AntiBot Gateway: Failed to initialize Model v2.0 from: {Path}", resolvedPathV2);
            }
        }
        else
        {
            _logger.LogWarning("AntiBot Gateway: Model v2.0 file not found at: {Path}. V2 will run in Heuristic fallback mode.", resolvedPathV2);
        }
    }

    private static string ResolveModelPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        string baseDir = AppContext.BaseDirectory;
        string combined = Path.Combine(baseDir, configuredPath);
        if (File.Exists(combined)) return combined;

        string currentDir = Directory.GetCurrentDirectory();
        combined = Path.Combine(currentDir, configuredPath);
        if (File.Exists(combined)) return combined;

        var candidates = new[] { currentDir, baseDir };
        foreach (var startDir in candidates)
        {
            string? dir = startDir;
            for (int i = 0; i < 6 && dir != null; i++)
            {
                var p1 = Path.Combine(dir, configuredPath);
                if (File.Exists(p1)) return p1;

                var p2 = Path.Combine(dir, "src", "TicketShield.AntiBot.Gateway", configuredPath);
                if (File.Exists(p2)) return p2;

                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        return configuredPath;
    }

    private static string ComputeFileSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Đánh giá rủi ro theo Model v1.0 (Baseline 6D).
    /// </summary>
    public AntiBotScoreResponse Evaluate(BotRiskVector vector)
    {
        var sw = Stopwatch.StartNew();
        float botProbability;

        if (_session != null)
        {
            float[] features = vector.ToArray();
            var inputTensor = new DenseTensor<float>(features, [1, 6]);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
            };

            using var results = _session.Run(inputs);
            var probTensor = results.First(r => r.Name == _outputName).AsTensor<float>();

            // probTensor[0, 1] là xác suất Bot
            botProbability = probTensor.GetValue(1);
        }
        else
        {
            botProbability = ComputeHeuristicScore(vector);
        }

        sw.Stop();
        double elapsedMs = sw.Elapsed.TotalMilliseconds;

        float allowThreshold = _options.AllowThreshold;
        float blockThreshold = _options.BlockThreshold;

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
            Details = $"Evaluated using {(_session != null ? "ONNX Runtime v1.0 (6D Embedded)" : "Fallback Heuristic v1")}"
        };
    }

    /// <summary>
    /// Đánh giá rủi ro theo Model v2.0 (3-Tier 11D - In-Process ONNX).
    /// </summary>
    public AntiBotScoreResponse EvaluateV2(BotRiskVectorV2 vector)
    {
        var sw = Stopwatch.StartNew();
        float botProbability;

        if (_sessionV2 != null)
        {
            float[] features = vector.ToArray();
            var inputTensor = new DenseTensor<float>(features, [1, 11]);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputNameV2, inputTensor)
            };

            using var results = _sessionV2.Run(inputs);
            var probTensor = results.First(r => r.Name == _outputNameV2).AsTensor<float>();

            // probTensor[0, 1] là xác suất Bot v2
            botProbability = probTensor.GetValue(1);
        }
        else
        {
            botProbability = ComputeHeuristicScoreV2(vector);
        }

        sw.Stop();
        double elapsedMs = sw.Elapsed.TotalMilliseconds;

        float allowThreshold = _options.AllowThreshold;
        float blockThreshold = _options.BlockThreshold;

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
            Details = $"Evaluated using {(_sessionV2 != null ? "ONNX Runtime v2.0 (11D Embedded)" : "Fallback Heuristic v2")}"
        };
    }

    /// <summary>
    /// Đánh giá đối chứng Shadow Mode song song giữa v1 và v2 (hỗ trợ Epic US-13.3, ML-13.3.4).
    /// </summary>
    public ShadowEvaluationResult EvaluateShadow(BotRiskVector v1, BotRiskVectorV2 v2)
    {
        var res1 = Evaluate(v1);
        var res2 = EvaluateV2(v2);

        var primary = _options.PrimaryModelIsV2 ? res2 : res1;
        var shadow = _options.PrimaryModelIsV2 ? res1 : res2;

        // Phân loại nhóm đối chứng phục vụ đo lường Shadow Mode
        string cohort;
        if (res1.Decision == "Allow" && res2.Decision == "Allow")
            cohort = "v1_allow_v2_allow";
        else if (res1.Decision == "Block" && res2.Decision == "Allow")
            cohort = "v1_block_v2_allow"; // Cứu nguy chặn nhầm (Brave Farbling)
        else if (res1.Decision == "Allow" && res2.Decision == "Block")
            cohort = "v1_allow_v2_block"; // Bắt thêm bot đối kháng (Stealth Bot)
        else if (res1.Decision == "Block" && res2.Decision == "Block")
            cohort = "v1_block_v2_block";
        else
            cohort = $"v1_{res1.Decision.ToLowerInvariant()}_v2_{res2.Decision.ToLowerInvariant()}";

        _logger.LogInformation(
            "AntiBot ShadowMode: Cohort={Cohort} | V1(Risk={R1}, Dec={D1}) vs V2(Risk={R2}, Dec={D2}) | Delta={Delta}",
            cohort, res1.RiskScore, res1.Decision, res2.RiskScore, res2.Decision,
            Math.Round(Math.Abs(res1.RiskScore - res2.RiskScore), 4));

        return new ShadowEvaluationResult
        {
            PrimaryResult = primary,
            ShadowResult = shadow,
            ShadowCohort = cohort
        };
    }

    private static float ComputeHeuristicScore(BotRiskVector v)
    {
        float score = 0.05f;
        if (v.IsWebDriver > 0.5f) score += 0.55f;
        if (v.TimeToCheckout >= 0 && v.TimeToCheckout < 1.0f) score += 0.35f;
        if (v.BurstRate10s > 10.0f) score += 0.30f;
        if (v.ConcurrentHold > 4.0f) score += 0.25f;
        if (v.DeviceAccountOverlap > 0.7f) score += 0.20f;
        if (v.ActionIntervalStdDev >= 0 && v.ActionIntervalStdDev < 20.0f) score += 0.20f;
        return Math.Clamp(score, 0.0f, 1.0f);
    }

    private static float ComputeHeuristicScoreV2(BotRiskVectorV2 v)
    {
        float score = 0.05f;
        // Tầng 1: Browser Telemetry
        if (v.IsWebDriver > 0.5f) score += 0.50f;
        if (v.VirtualGpuDetected > 0.5f) score += 0.45f;
        if (v.PrototypeTampered > 0.5f) score += 0.40f;
        // Brave Farbling (CanvasTampered > 0.5f nhưng nhịp bấm sinh học) KHÔNG phạt điểm!
        if (v.ActionIntervalStdDev >= 0 && v.ActionIntervalStdDev < 20.0f) score += 0.25f;

        // Tầng 2: Network Signals
        if (v.UaMatchLevel > 0.7f) score += 0.35f;
        if (v.NetworkInconsistencyScore > 0.5f) score += 0.30f;

        // Tầng 3: Business Dynamics
        if (v.TimeToCheckout >= 0 && v.TimeToCheckout < 1.0f) score += 0.35f;
        if (v.BurstRate10s > 10.0f) score += 0.30f;
        if (v.ConcurrentHold > 4.0f) score += 0.25f;
        if (v.DeviceAccountOverlap > 0.7f) score += 0.25f;

        return Math.Clamp(score, 0.0f, 1.0f);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _session?.Dispose();
            _sessionV2?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
