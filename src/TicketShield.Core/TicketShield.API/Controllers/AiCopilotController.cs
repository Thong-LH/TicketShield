using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Common.Models.Copilot;

namespace TicketShield.API.Controllers;

[ApiController]
[Route("api/v1/copilot")]
[Route("api/copilot")]
public class AiCopilotController : ControllerBase
{
    private readonly ITicketCopilotService _copilotService;
    private readonly ILogger<AiCopilotController> _logger;

    public AiCopilotController(ITicketCopilotService copilotService, ILogger<AiCopilotController> logger)
    {
        _copilotService = copilotService;
        _logger = logger;
    }

    /// <summary>
    /// Gửi câu hỏi đến Ticket Copilot và nhận phản hồi JSON (kèm thẻ vé gợi ý nếu có).
    /// </summary>
    [HttpPost("chat")]
    [ProducesResponseType(typeof(ApiResponse<CopilotChatResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CopilotChatResponse>>> Chat([FromBody] CopilotChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(ApiResponse<object>.FailureResponse("Nội dung câu hỏi không được để trống."));
        }

        var response = await _copilotService.ChatAsync(request, ct);
        return Ok(ApiResponse<CopilotChatResponse>.SuccessResponse(response));
    }

    /// <summary>
    /// Stream phản hồi câu hỏi qua Server-Sent Events (SSE) tạo hiệu ứng gõ chữ thời gian thực (typing effect).
    /// </summary>
    [HttpPost("chat/stream")]
    public async Task StreamChat([FromBody] CopilotChatRequest request, CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            var emptyErr = JsonSerializer.Serialize(new { error = "Nội dung câu hỏi không được để trống." });
            await Response.WriteAsync($"data: {emptyErr}\n\n", ct);
            await Response.Body.FlushAsync(ct);
            return;
        }

        try
        {
            await foreach (var token in _copilotService.StreamChatAsync(request, ct))
            {
                var payload = JsonSerializer.Serialize(new { token, done = false });
                await Response.WriteAsync($"data: {payload}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            var endPayload = JsonSerializer.Serialize(new { token = "", done = true });
            await Response.WriteAsync($"data: {endPayload}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Client closed connection
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while streaming Copilot chat");
            var errPayload = JsonSerializer.Serialize(new { error = "Đã xảy ra lỗi khi tạo phản hồi trực tiếp.", done = true });
            await Response.WriteAsync($"data: {errPayload}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>
    /// Kiểm tra trạng thái hoạt động của trợ lý Ticket Copilot.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "Online",
            service = "TicketShield GenAI Copilot",
            capabilities = new[] { "SearchResaleTickets", "CheckPriceCap", "SSE Streaming", "Prompt Sanitization" }
        });
    }
}
