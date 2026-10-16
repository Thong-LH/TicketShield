namespace MockOrganizer.API.Services;

/// <summary>
/// Bộ nhớ đệm phân tán / in-memory lưu trữ số lượng vé đang giữ (concurrent_hold) của Pipeline Anti-Bot.
/// Thay thế hoàn toàn việc query trực tiếp vào Database của Ban tổ chức.
/// Khóa lưu trữ chuẩn hóa: hold:{buyerEmail} với TTL mặc định 10 phút.
/// </summary>
public interface IPipelineHoldTracker
{
    /// <summary>
    /// Lấy số vé đang giữ đồng thời của người mua từ cache pipeline.
    /// </summary>
    Task<int> GetHoldCountAsync(string buyerEmail, CancellationToken ct = default);

    /// <summary>
    /// Cập nhật số vé đang giữ đồng thời vào cache pipeline (TTL mặc định 10 phút).
    /// </summary>
    Task SetHoldCountAsync(string buyerEmail, int count, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    /// Tăng hoặc giảm số vé đang giữ nguyên tử trong cache pipeline.
    /// </summary>
    Task<int> IncrementHoldCountAsync(string buyerEmail, int delta, TimeSpan? ttl = null, CancellationToken ct = default);
}
