using Microsoft.AspNetCore.Mvc;
using MockOrganizer.API.AntiBotScoring;

namespace MockOrganizer.API.Controllers;

[ApiController]
[Route("api/anti-bot")]
[Produces("application/json")]
public class AntiBotController : ControllerBase
{
    private readonly IAntiBotScoringService _scoringService;
    private readonly IHostEnvironment _env;
    private readonly ILogger<AntiBotController> _logger;

    public AntiBotController(
        IAntiBotScoringService scoringService,
        IHostEnvironment env,
        ILogger<AntiBotController> logger)
    {
        _scoringService = scoringService;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Chấm điểm độ rủi ro Bot dựa trên Vector 6 chiều tổng hợp (US-8.3).
    /// </summary>
    /// <param name="vector">Vector 6D bao gồm telemetry client & server</param>
    /// <returns>Xác suất Bot [0.0 - 1.0] và quyết định Allow / Challenge / Block</returns>
    [HttpPost("score")]
    [ProducesResponseType(typeof(AntiBotScoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<AntiBotScoreResponse> Score([FromBody] BotRiskVector vector)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = _scoringService.Evaluate(vector);
        _logger.LogInformation(
            "AntiBot Evaluated: RiskScore={Score}, Decision={Decision}, Latency={Latency}ms",
            result.RiskScore, result.Decision, result.InferenceTimeMs);

        return Ok(result);
    }

    /// <summary>
    /// Đo đạc hiệu năng suy luận (Benchmark) p99 latency đáp ứng SLA US-8.3 (< 2ms).
    /// Chỉ khả dụng trong môi trường Development để đảm bảo an toàn.
    /// </summary>
    /// <param name="iterations">Số lượt suy luận (Mặc định: 10,000, tối đa: 50,000)</param>
    [HttpGet("benchmark")]
    [ProducesResponseType(typeof(BenchmarkResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<BenchmarkResult> Benchmark([FromQuery] int iterations = 10000)
    {
        if (!_env.IsDevelopment())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Benchmark endpoint is only enabled in Development environment."
            });
        }

        int clampedIterations = Math.Clamp(iterations, 100, 50000);
        _logger.LogInformation("Bắt đầu chạy benchmark với {N} iterations...", clampedIterations);

        var result = _scoringService.RunBenchmark(clampedIterations);

        _logger.LogInformation(
            "Benchmark hoàn thành: P99={P99}ms, Mean={Mean}ms, RPS={RPS}, SLA_OK={SlaOk}",
            result.P99Ms, result.MeanMs, result.ThroughputRps, result.SlaOk);

        return Ok(result);
    }

    /// <summary>
    /// Kiểm tra trạng thái sẵn sàng của mô hình Anti-Bot.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "Healthy",
            service = "AntiBot ONNX Scoring Engine",
            timestamp = DateTime.UtcNow
        });
    }
}
