using TicketShield.AntiBot.Gateway.Models;

namespace TicketShield.AntiBot.Gateway.Services;

/// <summary>
/// Quản lý vòng đời và xác thực tính duy nhất của One-Time Cryptographic Nonce tại Gateway (US-13.2.3, SCRUM-376).
/// Đảm bảo mỗi Nonce chỉ được tiêu thụ đúng 1 lần (Atomic One-Time Consumption) chống lại Replay Attack.
/// </summary>
public interface INonceValidationService
{
    /// <summary>
    /// Đăng ký Nonce mới được phát hành kèm Session Token với thời hạn sống (TTL).
    /// </summary>
    Task RegisterNonceAsync(string nonce, Guid tierId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Xác thực và tiêu thụ Nonce (nguyên tử). Nếu Nonce đã được sử dụng trước đó, trả về kết quả Replay = true.
    /// </summary>
    Task<NonceConsumptionResult> ValidateAndConsumeNonceAsync(string nonce, Guid tierId, CancellationToken ct = default);
}
