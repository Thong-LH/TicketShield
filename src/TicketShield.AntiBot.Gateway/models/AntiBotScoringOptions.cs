namespace TicketShield.AntiBot.Gateway.Models;

public class AntiBotScoringOptions
{
    public const string SectionName = "AntiBotScoring";

    // Model v1.0 (Baseline)
    public string ModelPath { get; set; } = "models/anti_bot_model.onnx";
    public string InputNodeName { get; set; } = "float_input";
    public string OutputNodeName { get; set; } = "probabilities";
    public float AllowThreshold { get; set; } = 0.35f;
    public float BlockThreshold { get; set; } = 0.75f;
    public int GuardTimeoutMs { get; set; } = 20;

    // Model v2.0 (Epic US-13.3, Jira SCRUM-381)
    public string ModelV2Path { get; set; } = "models/anti_bot_model_v2.onnx";
    public string ModelV2MetadataPath { get; set; } = "models/model_v2_metadata.json";
    public string ModelV2InputNodeName { get; set; } = "float_input";
    public string ModelV2OutputNodeName { get; set; } = "probabilities";
    public string ExpectedV2Sha256 { get; set; } = "d647e87b5e23f3d6302ffb6ed597010388abc4e243a1c683f4293da2fa3f570c";

    // Shadow Mode Evaluation (Hỗ trợ ML-13.3.4)
    public bool EnableShadowMode { get; set; } = true;
    public bool PrimaryModelIsV2 { get; set; } = false;
}
