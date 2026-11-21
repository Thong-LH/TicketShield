namespace TicketShield.Infrastructure.Services.Copilot;

public class AiAssistantOptions
{
    public const string SectionName = "AiAssistant";

    public string Provider { get; set; } = "Gemini";
    public GeminiOptions Gemini { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();
}
