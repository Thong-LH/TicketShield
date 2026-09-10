namespace TicketShield.Application.Resale;

/// <summary>
/// Yêu cầu đăng bán vé lên thị trường sau khi đã xác thực và khóa vé thành công
/// </summary>
public sealed record PublishBody(
    string VerificationId,
    long ResalePrice,
    bool IsPrivate = false);
