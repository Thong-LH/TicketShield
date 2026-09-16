using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TicketShield.Identity.Application.Features.Auth.Commands.RefreshToken;
using TicketShield.Identity.Domain.Entities;
using TicketShield.Identity.Domain.Enums;
using TicketShield.Identity.Domain.Exceptions;
using TicketShield.Identity.Infrastructure.Persistence;
using TicketShield.Identity.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Features.Auth;

public class RefreshTokenCommandHandlerTests
{
    private static (IdentityDbContext db, JwtTokenGenerator tokenGen) CreateMocks()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new IdentityDbContext(options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "TicketShieldSuperSecretSecurityKeyForCapstoneProject2026",
                ["JwtSettings:Issuer"] = "TicketShield",
                ["JwtSettings:Audience"] = "TicketShieldApp",
                ["JwtSettings:ExpirationInMinutes"] = "15"
            })
            .Build();
        var tokenGen = new JwtTokenGenerator(config);

        return (db, tokenGen);
    }

    [Fact]
    public async Task Handle_WhenValidRefreshToken_ShouldRotateTokensAndRevokeOldToken()
    {
        // Arrange
        var (db, tokenGen) = CreateMocks();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "refreshuser@ticketshield.vn",
            FullName = "Refresh User",
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(user);

        var oldRefreshToken = tokenGen.GenerateRefreshToken(user.Id);
        db.RefreshTokens.Add(oldRefreshToken);
        await db.SaveChangesAsync();

        var handler = new RefreshTokenCommandHandler(db, tokenGen);
        var command = new RefreshTokenCommand(oldRefreshToken.Token, "192.168.1.1");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(string.IsNullOrEmpty(result.Data.AccessToken));
        Assert.False(string.IsNullOrEmpty(result.Data.RefreshToken));
        Assert.NotEqual(oldRefreshToken.Token, result.Data.RefreshToken);

        // Verify old token is revoked
        var oldInDb = await db.RefreshTokens.FirstOrDefaultAsync(r => r.Id == oldRefreshToken.Id);
        Assert.NotNull(oldInDb);
        Assert.True(oldInDb.IsRevoked);
        Assert.Equal(result.Data.RefreshToken, oldInDb.ReplacedByToken);
        Assert.Equal("192.168.1.1", oldInDb.RevokedByIp);

        // Verify new token is active
        var newInDb = await db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == result.Data.RefreshToken);
        Assert.NotNull(newInDb);
        Assert.True(newInDb.IsActive);
        Assert.Equal(user.Id, newInDb.UserId);
    }

    [Fact]
    public async Task Handle_WhenTokenIsExpiredOrRevoked_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var (db, tokenGen) = CreateMocks();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "expireduser@ticketshield.vn",
            FullName = "Expired User",
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(user);

        var expiredToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "expired_token_123",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-8),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.RefreshTokens.Add(expiredToken);
        await db.SaveChangesAsync();

        var handler = new RefreshTokenCommandHandler(db, tokenGen);
        var command = new RefreshTokenCommand("expired_token_123");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None));
    }
}
