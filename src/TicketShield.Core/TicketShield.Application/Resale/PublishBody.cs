namespace TicketShield.Application.Resale;

/// <summary>
/// Yêu cầu đăng bán MỘT vé lên thị trường sau khi phiên OTP của chính vé đó đã xác thực và khóa thành công.
/// Cố tình không chứa tham số Bundle: gói vé chỉ được tạo qua
/// <see cref="BulkPublishBody"/> (mỗi item mang một verificationId đã verify riêng).
/// </summary>
public sealed record PublishBody(
    string VerificationId,
    long ResalePrice,
    bool IsPrivate = false);
