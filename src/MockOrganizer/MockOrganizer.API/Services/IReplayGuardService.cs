using MockOrganizer.API.Controllers;

namespace MockOrganizer.API.Services;

/// <summary>
/// Mã lỗi trả về trong body HTTP 409 khi checkout bị chặn replay (SCRUM-247).
/// </summary>
public static class ReplayRejectionCodes
{
    public const string TierMismatch = "TIER_MISMATCH";
    public const string ClockSkew = "CLOCK_SKEW";
    public const string Replay = "REPLAY";
}

public sealed record ReplayGuardResult(bool IsAllowed, string? Code = null, string? Message = null)
{
    public static readonly ReplayGuardResult Allowed = new(true);

    public static ReplayGuardResult Reject(string code, string message) => new(false, code, message);
}

public interface IReplayGuardService
{
    /// <summary>
    /// Kiểm tra checkout trước Anti-Bot Guard: token khớp hạng vé, giờ gửi lệch tối đa 5 giây,
    /// nonce chưa dùng trong 10 phút. Không có nonce thì chỉ kiểm tra hạng vé.
    /// </summary>
    Task<ReplayGuardResult> ValidateAsync(CheckoutRequest request, CancellationToken ct = default);
}
