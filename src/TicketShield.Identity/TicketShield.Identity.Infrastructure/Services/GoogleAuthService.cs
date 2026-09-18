using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketShield.Identity.Application.Common.Interfaces;

namespace TicketShield.Identity.Infrastructure.Services;

public class GoogleAuthService : IGoogleAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(IConfiguration configuration, ILogger<GoogleAuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GoogleUserInfo?> VerifyGoogleTokenAsync(string idToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var clientId = _configuration["GoogleAuth:ClientId"];
            var settings = new GoogleJsonWebSignature.ValidationSettings();

            // Only enforce audience check if a real non-mock Google Client ID is configured
            if (!string.IsNullOrWhiteSpace(clientId) && !clientId.StartsWith("mock-", StringComparison.OrdinalIgnoreCase))
            {
                settings.Audience = new[] { clientId };
            }

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            if (payload == null)
            {
                _logger.LogWarning("[GoogleAuth] Payload trả về null khi xác thực Google IdToken.");
                return null;
            }

            return new GoogleUserInfo
            {
                GoogleId = payload.Subject,
                Email = payload.Email,
                Name = payload.Name,
                Picture = payload.Picture
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuth] Xác thực Google IdToken thất bại: {Message}", ex.Message);
            return null;
        }
    }
}
