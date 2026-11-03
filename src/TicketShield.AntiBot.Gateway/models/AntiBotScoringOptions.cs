namespace TicketShield.AntiBot.Gateway.Models;

public class AntiBotScoringOptions
{
    public const string SectionName = "AntiBotScoring";

    public string ModelPath { get; set; } = "models/anti_bot_model.onnx";
    public string InputNodeName { get; set; } = "float_input";
    public string OutputNodeName { get; set; } = "probabilities";
    public float AllowThreshold { get; set; } = 0.35f;
    public float BlockThreshold { get; set; } = 0.75f;
    public int GuardTimeoutMs { get; set; } = 20;
}
