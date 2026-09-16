using TicketShield.Identity.Domain.Entities;

namespace TicketShield.Identity.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    /// <summary>
    /// Sinh JWT Access Token có hiệu lực 15 phút theo chuẩn Microservice Blueprint
    /// </summary>
    (string AccessToken, int ExpiresInSeconds) GenerateAccessToken(User user);

    /// <summary>
    /// Sinh Refresh Token 7 ngày phục vụ xoay vòng token (Token Rotation)
    /// </summary>
    RefreshToken GenerateRefreshToken(Guid userId, string? ipAddress = null);
}
