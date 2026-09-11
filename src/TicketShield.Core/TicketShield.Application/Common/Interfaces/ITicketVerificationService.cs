using TicketShield.Application.Resale;

namespace TicketShield.Application.Common.Interfaces;

/// <summary>
/// Service trừu tượng điều phối quy trình xác thực vé qua gRPC với Nhà tổ chức (Organizer)
/// và quản lý tin bán lại (Resale Listings).
/// </summary>
public interface ITicketVerificationService
{
    /// <summary>
    /// Bắt đầu quy trình xác thực vé: Yêu cầu Nhà tổ chức gửi mã OTP về email của chủ vé gốc.
    /// </summary>
    Task<VerificationResult> Request(string seller, string key, string ticket, CancellationToken ct);

    /// <summary>
    /// Gửi lại mã OTP xác thực qua email nếu chưa nhận được.
    /// </summary>
    Task<VerificationResult> Resend(string seller, string id, string key, CancellationToken ct);

    /// <summary>
    /// Nhập OTP xác thực và yêu cầu Nhà tổ chức thực hiện Khóa vé (Lock) chống gian lận.
    /// </summary>
    Task<VerificationResult> Confirm(string seller, string id, string key, string otp, CancellationToken ct);

    /// <summary>
    /// Tra cứu trạng thái phiên xác thực vé.
    /// </summary>
    Task<VerificationResult> Get(string seller, string id, CancellationToken ct);

    /// <summary>
    /// Đóng / hủy phiên xác thực vé trước khi đăng bán.
    /// </summary>
    Task<VerificationResult> Close(string seller, string id, string key, CancellationToken ct);

    /// <summary>
    /// Đăng bán vé lên sàn TicketShield sau khi vé đã được xác thực và khóa thành công.
    /// </summary>
    Task<VerificationResult> Publish(string seller, string key, PublishBody body, CancellationToken ct);

    /// <summary>
    /// Hủy tin đăng bán vé và yêu cầu Nhà tổ chức mở khóa vé (Release Lock) trả lại cho chủ vé.
    /// </summary>
    Task<VerificationResult> Cancel(string seller, string id, string key, CancellationToken ct);

    /// <summary>
    /// Hủy tin đăng bán vé qua ListingId: tra cứu phiên gRPC và gọi unlock nếu tồn tại.
    /// Dùng cho REST endpoint SCRUM-33 (POST /api/v1/resale-listings/{id}/cancel).
    /// </summary>
    Task CancelByListingId(string seller, Guid listingId, string key, CancellationToken ct);

    /// <summary>
    /// Lấy danh sách vé công khai đang niêm yết trên thị trường.
    /// </summary>
    Task<List<ListingResult>> Marketplace(int page, int size, CancellationToken ct);

    /// <summary>
    /// Tiến trình ngầm phục hồi các giao dịch phân tán đang bị treo (Reconciliation).
    /// </summary>
    Task RecoverPending(CancellationToken ct);
}

/// <summary>
/// Bí danh tương thích cho ITicketVerificationService
/// </summary>
public interface ITicketResaleWorkflow : ITicketVerificationService
{
}
