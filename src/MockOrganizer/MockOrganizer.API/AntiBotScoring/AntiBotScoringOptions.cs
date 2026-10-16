namespace MockOrganizer.API.AntiBotScoring;

/// <summary>
/// Cấu hình cho Anti-Bot Scoring Service.
/// </summary>
public class AntiBotScoringOptions
{
    public const string SectionName = "AntiBotScoring";

    /// <summary>
    /// Đường dẫn tương đối hoặc tuyệt đối tới file mô hình ONNX.
    /// Mặc định: "models/anti_bot_model.onnx".
    /// </summary>
    public string ModelPath { get; set; } = "models/anti_bot_model.onnx";

    /// <summary>
    /// Tên input node trong file ONNX. Mặc định: "float_input".
    /// </summary>
    public string InputNodeName { get; set; } = "float_input";

    /// <summary>
    /// Tên output node chứa xác suất trong file ONNX. Mặc định: "probabilities".
    /// </summary>
    public string OutputNodeName { get; set; } = "probabilities";

    /// <summary>
    /// Ngưỡng xác suất cho phép giao dịch vượt qua mà không cần thử thách (mặc định < 0.35).
    /// </summary>
    public float AllowThreshold { get; set; } = 0.35f;

    /// <summary>
    /// Ngưỡng xác suất chặn giao dịch bot ngay lập tức (mặc định >= 0.75).
    /// </summary>
    public float BlockThreshold { get; set; } = 0.75f;
}
