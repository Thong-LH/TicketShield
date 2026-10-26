using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TicketShield.AntiBot.Gateway.Models;

namespace TicketShield.AntiBot.Gateway.Services;

public interface IAntiBotScoringService
{
    AntiBotScoreResponse Evaluate(BotRiskVector vector);
}

public class AntiBotScoringService : IAntiBotScoringService, IDisposable
{
    private readonly ILogger<AntiBotScoringService> _logger;
    private readonly AntiBotScoringOptions _options;
    private readonly InferenceSession? _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private bool _disposed;

    public AntiBotScoringService(
        IOptions<AntiBotScoringOptions> options,
        ILogger<AntiBotScoringService> logger)
    {
        _logger = logger;
        _options = options.Value;
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
            _logger.LogInformation("AntiBot Gateway: Initialized ONNX InferenceSession from: {Path}", resolvedPath);
        }
        else
        {
            _logger.LogWarning("AntiBot Gateway: Model file not found at: {Path}. Running in Fallback Heuristic mode.", resolvedPath);
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
            Details = $"Evaluated using {(_session != null ? "ONNX Runtime (Embedded)" : "Fallback Heuristic")}"
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
