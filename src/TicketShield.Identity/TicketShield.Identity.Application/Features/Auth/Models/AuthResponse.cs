namespace TicketShield.Identity.Application.Features.Auth.Models;

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; } = 900; // 15 phút (900s)
    public UserProfileDto User { get; set; } = null!;
}
