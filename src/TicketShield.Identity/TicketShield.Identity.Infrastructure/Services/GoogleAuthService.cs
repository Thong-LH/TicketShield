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
            var webClientId = _configuration["GoogleAuth:ClientId"]
                              ?? _configuration["GOOGLE_AUTH_CLIENT_ID"]
                              ?? _configuration["GOOGLEAUTH__CLIENTID"];
            var androidClientId = _configuration["GoogleAuth:AndroidClientId"]
                                  ?? _configuration["GOOGLE_AUTH_ANDROID_CLIENT_ID"]
                                  ?? _configuration["GOOGLEAUTH__ANDROIDCLIENTID"];

            var audiences = new List<string>();
            if (!string.IsNullOrWhiteSpace(webClientId) && !webClientId.StartsWith("mock-", StringComparison.OrdinalIgnoreCase))
            {
                audiences.Add(webClientId);
            }
            if (!string.IsNullOrWhiteSpace(androidClientId) && !androidClientId.StartsWith("mock-", StringComparison.OrdinalIgnoreCase))
            {
                audiences.Add(androidClientId);
            }

            var settings = new GoogleJsonWebSignature.ValidationSettings();
            if (audiences.Count > 0)
            {
                settings.Audience = audiences;
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
