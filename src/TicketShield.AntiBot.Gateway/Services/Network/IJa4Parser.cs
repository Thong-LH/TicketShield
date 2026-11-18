using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Giao diện phân tích cú pháp chuỗi JA4 Fingerprint từ Reverse Proxy Header (X-JA4, CF-JA4).
/// Chuẩn đặc tả: Fox-IT / John Althouse (JA4+ Database).
/// </summary>
public interface IJa4Parser
{
    /// <summary>
    /// Thử phân tích cú pháp chuỗi JA4 thô thành đối tượng <see cref="ParsedJa4"/>.
    /// Đảm bảo không ném ngoại lệ (Exception-safe) và tối ưu hóa bộ nhớ (Zero heap allocation khi validate).
    /// </summary>
    /// <param name="rawJa4">Chuỗi JA4 nhận được từ header (ví dụ: "t13d1516h2_8daaf6152771_02711d0e519e").</param>
    /// <param name="parsed">Đối tượng phân tích nếu thành công, hoặc <see cref="ParsedJa4.Empty"/> nếu thất bại.</param>
    /// <returns>True nếu chuỗi JA4 hợp lệ đúng quy chuẩn; ngược lại là false.</returns>
    bool TryParse(string? rawJa4, out ParsedJa4 parsed);

    /// <summary>
    /// Phân tích chuỗi JA4 thô, trả về <see cref="ParsedJa4.Empty"/> nếu chuỗi rỗng hoặc không hợp lệ.
    /// </summary>
    /// <param name="rawJa4">Chuỗi JA4 thô.</param>
    /// <returns>Đối tượng <see cref="ParsedJa4"/>.</returns>
    ParsedJa4 ParseOrDefault(string? rawJa4);
}
