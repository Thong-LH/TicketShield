using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace TicketShield.API.Resale;

public static class ResaleApiRegistration
{
    public static void AddResaleAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var key = configuration["ResaleJwt:SigningKey"] ?? "";
        var issuer = configuration["ResaleJwt:Issuer"] ?? "";
        var audience = configuration["ResaleJwt:Audience"] ?? "";
        if (key.Length < 32 || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("ResaleJwt requires an issuer, audience and signing key of at least 32 characters.");
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters {
                ValidateIssuer = true, ValidIssuer = issuer, ValidateAudience = true, ValidAudience = audience,
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromSeconds(15)
            };
        });
        services.AddAuthorization();
    }
}
