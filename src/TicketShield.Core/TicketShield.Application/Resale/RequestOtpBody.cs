namespace TicketShield.Application.Resale;

/// <summary>
/// Yêu cầu gửi mã xác thực OTP cho mã vé gốc
/// </summary>
public sealed record RequestOtpBody(string TicketCode);
