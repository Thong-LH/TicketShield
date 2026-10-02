namespace TicketShield.Application.Resale;

/// <summary>
/// Yêu cầu đăng bán vé lên thị trường sau khi đã xác thực và khóa vé thành công.
/// Hỗ trợ gán trực tiếp thông tin Bundle ngay từ lệnh INSERT đầu tiên (SCRUM-167 / BE-CORE-5.2.2).
/// </summary>
public sealed record PublishBody(
    string VerificationId,
    long ResalePrice,
    bool IsPrivate = false,
    Guid? BundleId = null,
    bool IsBundleAllOrNothing = false,
    int BundleTotalTickets = 1);
