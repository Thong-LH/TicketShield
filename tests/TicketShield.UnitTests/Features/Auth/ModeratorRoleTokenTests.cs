using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TicketShield.Identity.Domain.Entities;
using TicketShield.Identity.Domain.Enums;
using TicketShield.Identity.Infrastructure.Services;

namespace TicketShield.UnitTests.Features.Auth;

public class ModeratorRoleTokenTests
{
    private const string Secret = "TicketShieldSuperSecretSecurityKeyForCapstoneProject2026";
    private const string Issuer = "TicketShield";
    private const string Audience = "TicketShieldApp";

    [Fact]
    public void UserRole_Moderator_IsFour_AndExistingValuesStay()
    {
        Assert.Equal(4, (int)UserRole.Moderator);
        Assert.Equal(1, (int)UserRole.User);
        Assert.Equal(2, (int)UserRole.Organizer);
        Assert.Equal(3, (int)UserRole.Admin);
    }

    [Fact]
    public void GenerateAccessToken_WhenUserIsModerator_RoleClaimAndCallerRoleAreModerator()
    {
        var tokenGen = CreateGenerator();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "moderator@ticketshield.vn",
            FullName = "Moderator",
            Role = UserRole.Moderator
        };

        var (firstToken, _) = tokenGen.GenerateAccessToken(user);
        var (secondToken, _) = tokenGen.GenerateAccessToken(user);

        AssertRole(firstToken, "Moderator");
        AssertRole(secondToken, "Moderator");
    }

    [Fact]
    public void GenerateAccessToken_WhenUserIsAdmin_CallerRoleStaysAdmin()
    {
        var tokenGen = CreateGenerator();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@ticketshield.vn",
            FullName = "Admin",
            Role = UserRole.Admin
        };

        var (token, _) = tokenGen.GenerateAccessToken(user);

        AssertRole(token, "Admin");
    }

    private static void AssertRole(string accessToken, string expectedRole)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);
        var tokenRole = jwt.Claims.Single(c => c.Type is "role" or ClaimTypes.Role).Value;
        Assert.Equal(expectedRole, tokenRole);

        var principal = handler.ValidateToken(accessToken, IdentityTokenParameters(), out _);
        var callerRole = principal.FindFirstValue(ClaimTypes.Role)
            ?? principal.FindFirstValue("role");
        Assert.Equal(expectedRole, callerRole);
    }

    private static TokenValidationParameters IdentityTokenParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
        ClockSkew = TimeSpan.FromMinutes(5)
    };

    private static JwtTokenGenerator CreateGenerator()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = Secret,
                ["JwtSettings:Issuer"] = Issuer,
                ["JwtSettings:Audience"] = Audience,
                ["JwtSettings:ExpirationInMinutes"] = "15"
            })
            .Build();
        return new JwtTokenGenerator(config);
    }
}
