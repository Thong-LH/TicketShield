using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Infrastructure.Services;

public class GoogleAuthService : IGoogleAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(IConfiguration configuration, ILogger<GoogleAuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GoogleUserInfo> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            throw new BusinessRuleViolationException("Google IdToken không được để trống.");
        }

        // Mock test bypass mode for automated testing without real Google client
        if (idToken.StartsWith("mock-google-token-"))
        {
            var parts = idToken.Split(':');
            var mockEmail = parts.Length > 1 ? parts[1] : "googleuser@ticketshield.vn";
            var mockName = parts.Length > 2 ? parts[2] : "Google Test User";
            var mockGoogleId = "google-id-" + Guid.NewGuid().ToString("N");

            return new GoogleUserInfo
            {
                GoogleId = mockGoogleId,
                Email = mockEmail,
                FullName = mockName,
                PictureUrl = "https://lh3.googleusercontent.com/a/default-user"
            };
        }

        try
        {
            var clientId = _configuration["GoogleAuth:ClientId"];
            var settings = new GoogleJsonWebSignature.ValidationSettings();
            if (!string.IsNullOrEmpty(clientId))
            {
                settings.Audience = new[] { clientId };
            }

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            return new GoogleUserInfo
            {
                GoogleId = payload.Subject,
                Email = payload.Email,
                FullName = payload.Name ?? payload.Email,
                PictureUrl = payload.Picture
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google OAuth token validation failed.");
            throw new BusinessRuleViolationException("Xác thực mã Google Token thất bại hoặc mã đã hết hạn.");
        }
    }
}
