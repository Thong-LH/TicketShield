namespace MockOrganizer.API.AntiBotScoring;

/// <summary>
/// Cấu hình ngưỡng phân luồng quyết định 3 tầng Anti-Bot (US-8.4, SCRUM-260).
/// </summary>
public record AntiBotPolicyConfig
{
    /// <summary>
    /// Ngưỡng rủi ro cho phép đi tiếp (ALLOW) mà không bị thách thức. Mặc định: 0.35.
    /// </summary>
    public float AllowThreshold { get; init; } = 0.35f;

    /// <summary>
    /// Ngưỡng rủi ro chặn dứt khoát (BLOCK) trả về HTTP 429. Mặc định: 0.75.
    /// </summary>
    public float BlockThreshold { get; init; } = 0.75f;

    /// <summary>
    /// Chế độ thử thách xác thực khi rơi vào diện nghi vấn (Tier 2 Challenge):
    /// "recaptcha" (Google reCAPTCHA v2 hình ảnh - mặc định), "turnstile" (Cloudflare Turnstile), "sequential" (Tuần tự cả 2).
    /// </summary>
    public string ChallengeMode { get; init; } = "recaptcha";

    /// <summary>
    /// Thời điểm cập nhật cấu hình lần gần nhất (UTC).
    /// </summary>
    public DateTime LastUpdatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Người hoặc hệ thống cập nhật.
    /// </summary>
    public string UpdatedBy { get; init; } = "System";
}

public interface IAntiBotPolicyManager
{
    /// <summary>
    /// Lấy cấu hình ngưỡng hiện tại (hỗ trợ phân biệt theo eventId nếu có cấu hình riêng).
    /// </summary>
    AntiBotPolicyConfig GetPolicy(string? eventId = null);

    /// <summary>
    /// Cập nhật ngưỡng động trong runtime (Zero-downtime, không cần restart server).
    /// Ràng buộc: 0.05 &lt;= allowThreshold &lt; blockThreshold &lt;= 0.99.
    /// </summary>
    bool TryUpdatePolicy(
        float allowThreshold,
        float blockThreshold,
        out string? errorMessage,
        string? eventId = null,
        string? updatedBy = null,
        string? challengeMode = null);

    /// <summary>
    /// Khôi phục về ngưỡng mặc định ban đầu (Allow = 0.35, Block = 0.75).
    /// </summary>
    void ResetToDefault(string? eventId = null);
}
