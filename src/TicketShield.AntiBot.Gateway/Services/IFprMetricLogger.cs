using TicketShield.AntiBot.Gateway.Models;

namespace TicketShield.AntiBot.Gateway.Services;

/// <summary>
/// Giao diện ghi log cấu trúc JSON Lines phi đồng bộ (Non-blocking Structured FPR Logger)
/// (Epic US-13.3, Jira SCRUM-382).
/// </summary>
public interface IFprMetricLogger
{
    /// <summary>
    /// Đẩy 1 bản ghi đánh giá vào hàng đợi phi đồng bộ (Non-blocking, SLA 0ms overhead).
    /// </summary>
    /// <returns>True nếu đẩy vào channel thành công, False nếu bị từ chối hoặc log bị vô hiệu hoá.</returns>
    bool LogEvaluation(ShadowEvaluationLogEntry entry);

    /// <summary>
    /// Đẩy sạch toàn bộ dữ liệu đang chờ trong Channel ra đĩa (phục vụ Graceful Shutdown và Integration Tests).
    /// </summary>
    Task FlushAsync(CancellationToken ct = default);
}
