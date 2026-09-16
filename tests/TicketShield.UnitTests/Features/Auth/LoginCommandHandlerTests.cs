using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TicketShield.Identity.Application.Features.Auth.Commands.Login;
using TicketShield.Identity.Domain.Entities;
using TicketShield.Identity.Domain.Enums;
using TicketShield.Identity.Domain.Exceptions;
using TicketShield.Identity.Infrastructure.Persistence;
using TicketShield.Identity.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Features.Auth;

public class LoginCommandHandlerTests
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
    public async Task Handle_WhenCredentialsAreValid_ShouldReturnAccessTokenAndRefreshToken()
    {
        // Arrange
        var (db, tokenGen) = CreateMocks();
        var passwordHash = BCrypt.Net.BCrypt.HashPassword("ValidPassword123!");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "loginuser@ticketshield.vn",
            FullName = "Login User",
            PasswordHash = passwordHash,
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new LoginCommandHandler(db, tokenGen);
        var command = new LoginCommand("loginuser@ticketshield.vn", "ValidPassword123!");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(user.Id, result.Data.User.Id);
        Assert.False(string.IsNullOrEmpty(result.Data.AccessToken));
        Assert.False(string.IsNullOrEmpty(result.Data.RefreshToken));
        Assert.Equal(900, result.Data.ExpiresIn);

        var tokenInDb = await db.RefreshTokens.FirstOrDefaultAsync(r => r.UserId == user.Id);
        Assert.NotNull(tokenInDb);
        Assert.True(tokenInDb.IsActive);
    }

    [Fact]
    public async Task Handle_WhenPasswordIsIncorrect_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var (db, tokenGen) = CreateMocks();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "wrongpass@ticketshield.vn",
            FullName = "Wrong Pass User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("CorrectPassword123!"),
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new LoginCommandHandler(db, tokenGen);
        var command = new LoginCommand("wrongpass@ticketshield.vn", "IncorrectPassword999!");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenAccountIsInactive_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var (db, tokenGen) = CreateMocks();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "inactive@ticketshield.vn",
            FullName = "Inactive User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("ValidPassword123!"),
            Role = UserRole.User,
            IsActive = false
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new LoginCommandHandler(db, tokenGen);
        var command = new LoginCommand("inactive@ticketshield.vn", "ValidPassword123!");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None));
    }
}
