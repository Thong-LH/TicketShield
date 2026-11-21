namespace TicketShield.Application.Common.Models.Copilot;

public class CopilotChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}
