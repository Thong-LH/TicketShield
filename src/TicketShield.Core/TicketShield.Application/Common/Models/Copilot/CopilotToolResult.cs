namespace TicketShield.Application.Common.Models.Copilot;

public class CopilotToolResult
{
    public string ToolName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? ErrorMessage { get; set; }
}
