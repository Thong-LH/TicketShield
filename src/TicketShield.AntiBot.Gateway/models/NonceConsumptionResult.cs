namespace TicketShield.AntiBot.Gateway.Models;

/// <summary>
/// Kết quả tiêu thụ One-Time Cryptographic Nonce tại Gateway (US-13.2.3, SCRUM-376).
/// </summary>
public record NonceConsumptionResult
{
    public bool IsSuccess { get; init; } = false;
    public bool IsReplay { get; init; } = false;
    public bool IsExpired { get; init; } = false;
    public string Message { get; init; } = string.Empty;

    public static NonceConsumptionResult Success() =>
        new() { IsSuccess = true, Message = "Nonce validated and consumed successfully." };

    public static NonceConsumptionResult Replay(string message = "Nonce has already been consumed (replay attack detected).") =>
        new() { IsReplay = true, Message = message };

    public static NonceConsumptionResult Expired(string message = "Nonce not found or expired.") =>
        new() { IsExpired = true, Message = message };

    public static NonceConsumptionResult Failed(string message) =>
        new() { Message = message };
}
