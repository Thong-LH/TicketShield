namespace TicketShield.Application.Common.Models.Copilot;

public class CopilotChatRequest
{
    public string Message { get; set; } = string.Empty;
    public List<CopilotChatMessage>? History { get; set; }
    public List<CopilotChatMessage>? ConversationHistory { get; set; }
    public string? CurrentEventContext { get; set; }

    public List<CopilotChatMessage> GetEffectiveHistory() =>
        History ?? ConversationHistory ?? new List<CopilotChatMessage>();
}
