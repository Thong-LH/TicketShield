using Microsoft.AspNetCore.Mvc;
using MockOrganizer.API.AntiBotScoring;

namespace MockOrganizer.API.Controllers;

public record UpdateAntiBotPolicyRequest
{
    public float AllowThreshold { get; init; } = 0.35f;
    public float BlockThreshold { get; init; } = 0.75f;
    public string? ChallengeMode { get; init; }
    public string? EventId { get; init; }
    public string? UpdatedBy { get; init; }
}

[ApiController]
[Route("api/anti-bot")]
[Produces("application/json")]
public class AntiBotController : ControllerBase
{
    private readonly IAntiBotScoringService _scoringService;
    private readonly IAntiBotPolicyManager _policyManager;
    private readonly IAntiBotAnalyticsCollector _analyticsCollector;
    private readonly IHostEnvironment _env;
    private readonly ILogger<AntiBotController> _logger;

    public AntiBotController(
        IAntiBotScoringService scoringService,
        IAntiBotPolicyManager policyManager,
        IAntiBotAnalyticsCollector analyticsCollector,
        IHostEnvironment env,
        ILogger<AntiBotController> logger)
    {
        _scoringService = scoringService;
        _policyManager = policyManager;
        _analyticsCollector = analyticsCollector;
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
    /// Lấy cấu hình ngưỡng phân luồng quyết định hiện tại (US-8.4, SCRUM-260).
    /// </summary>
    [HttpGet("policy")]
    [ProducesResponseType(typeof(AntiBotPolicyConfig), StatusCodes.Status200OK)]
    public ActionResult<AntiBotPolicyConfig> GetPolicy([FromQuery] string? eventId = null)
    {
        var policy = _policyManager.GetPolicy(eventId);
        return Ok(policy);
    }

    /// <summary>
    /// Cập nhật ngưỡng động trong runtime không cần restart (US-8.4, SCRUM-260).
    /// </summary>
    [HttpPut("policy")]
    [ProducesResponseType(typeof(AntiBotPolicyConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<AntiBotPolicyConfig> UpdatePolicy([FromBody] UpdateAntiBotPolicyRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        bool success = _policyManager.TryUpdatePolicy(
            request.AllowThreshold,
            request.BlockThreshold,
            out string? errorMessage,
            request.EventId,
            request.UpdatedBy,
            request.ChallengeMode);

        if (!success)
        {
            return BadRequest(new { message = errorMessage });
        }

        var updatedPolicy = _policyManager.GetPolicy(request.EventId);
        return Ok(updatedPolicy);
    }

    /// <summary>
    /// Đặt lại ngưỡng về mặc định ban đầu (Allow = 0.35, Block = 0.75).
    /// </summary>
    [HttpPost("policy/reset")]
    [ProducesResponseType(typeof(AntiBotPolicyConfig), StatusCodes.Status200OK)]
    public ActionResult<AntiBotPolicyConfig> ResetPolicy([FromQuery] string? eventId = null)
    {
        _policyManager.ResetToDefault(eventId);
        return Ok(_policyManager.GetPolicy(eventId));
    }

    /// <summary>
    /// Lấy báo cáo phân bổ lưu lượng và danh sách giao dịch gần nhất cho Dashboard BTC (US-8.4, SCRUM-259).
    /// </summary>
    [HttpGet("analytics")]
    [ProducesResponseType(typeof(AntiBotAnalyticsSummary), StatusCodes.Status200OK)]
    public ActionResult<AntiBotAnalyticsSummary> GetAnalytics([FromQuery] string? eventId = null)
    {
        var summary = _analyticsCollector.GetSummary(eventId);
        return Ok(summary);
    }

    /// <summary>
    /// Đặt lại số liệu thống kê an ninh (phục vụ demo / testing).
    /// </summary>
    [HttpPost("analytics/reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult ResetAnalytics([FromQuery] string? eventId = null)
    {
        _analyticsCollector.Reset(eventId);
        return Ok(new { message = "Analytics reset successfully." });
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
