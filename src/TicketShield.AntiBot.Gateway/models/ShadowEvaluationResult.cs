namespace TicketShield.AntiBot.Gateway.Models;

/// <summary>
/// Kết quả đánh giá đối chứng giữa Model v1.0 và Model v2.0 trong chế độ Shadow Mode (Epic US-13.3, Jira SCRUM-381 / SCRUM-382).
/// </summary>
public record ShadowEvaluationResult
{
    /// <summary>
    /// Kết quả của mô hình chính (đang quyết định traffic thực tế).
    /// </summary>
    public required AntiBotScoreResponse PrimaryResult { get; init; }

    /// <summary>
    /// Kết quả của mô hình chạy ngầm (mô hình v2.0 đánh giá song song không ảnh hưởng traffic).
    /// </summary>
    public required AntiBotScoreResponse ShadowResult { get; init; }

    /// <summary>
    /// Phân loại nhóm đối chứng (Shadow Cohort):
    /// - "v1_allow_v2_allow": Cả 2 mô hình đồng thuận người dùng hợp lệ
    /// - "v1_block_v2_allow": v1 chặn nhầm nhưng v2 cứu nguy (Ví dụ: Brave Farbling)
    /// - "v1_allow_v2_block": v1 để lọt nhưng v2 bắt trúng (Ví dụ: Stealth Bot)
    /// - "v1_block_v2_block": Cả 2 mô hình đồng thuận chặn bot
    /// </summary>
    public required string ShadowCohort { get; init; }

    /// <summary>
    /// Quyết định giữa 2 mô hình có đồng thuận hay không.
    /// </summary>
    public bool DecisionMatches => PrimaryResult.Decision == ShadowResult.Decision;

    /// <summary>
    /// Độ lệch điểm rủi ro giữa 2 mô hình (|Risk_Primary - Risk_Shadow|).
    /// </summary>
    public float RiskScoreDelta => (float)Math.Round(Math.Abs(PrimaryResult.RiskScore - ShadowResult.RiskScore), 4);
}
