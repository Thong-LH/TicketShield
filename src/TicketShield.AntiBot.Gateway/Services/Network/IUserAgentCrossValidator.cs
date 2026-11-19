using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Dịch vụ đối soát chéo User-Agent giữa JavaScript Client thu thập và HTTP Header (US-13.2.4, SCRUM-377).
/// Phát hiện các hành vi giả mạo User-Agent, Rotating Proxy Header Injection, và Telemetry Replay.
/// </summary>
public interface IUserAgentCrossValidator
{
    UserAgentCrossValidationResult Validate(string? clientUserAgent, string? headerUserAgent);
}
