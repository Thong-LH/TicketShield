namespace TicketShield.Application.Common.Models.Copilot;

public class CopilotChatResponse
{
    public string Reply { get; set; } = string.Empty;
    public List<CopilotTicketItemDto>? SuggestedTickets { get; set; }
    public CopilotPriceCapResultDto? PriceCapInfo { get; set; }
    public List<string>? ExecutedTools { get; set; }
}
