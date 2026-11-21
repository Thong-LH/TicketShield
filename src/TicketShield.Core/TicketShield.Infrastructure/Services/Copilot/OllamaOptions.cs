namespace TicketShield.Infrastructure.Services.Copilot;

public class OllamaOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434/v1";
    public string Model { get; set; } = "qwen2.5:3b";
}
