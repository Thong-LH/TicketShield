namespace TicketShield.Application.Resale;

/// <summary>
/// Yêu cầu xác nhận mã OTP và khóa vé
/// </summary>
public sealed class ConfirmOtpBody
{
    public string Otp { get; set; } = string.Empty;
}
