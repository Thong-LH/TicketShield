using Microsoft.AspNetCore.Http;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Giao diện dịch vụ đối soát W3C Client Hints và Sec-Fetch headers với User-Agent (NET-13.1.3).
/// </summary>
public interface IClientHintsValidator
{
    /// <summary>
    /// Bóc tách và đối soát Client Hints từ HttpContext.
    /// </summary>
    ClientHintsProfile Validate(HttpContext context);

    /// <summary>
    /// Bóc tách và đối soát Client Hints từ danh sách headers (hỗ trợ kiểm thử độc lập).
    /// </summary>
    ClientHintsProfile Validate(IHeaderDictionary headers);
}
