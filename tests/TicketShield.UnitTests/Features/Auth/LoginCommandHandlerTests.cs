using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TicketShield.Application.Features.Auth.Commands.Login;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Features.Auth;

public class LoginCommandHandlerTests
{
    private static (TicketShieldDbContext db, BcryptPasswordHasher hasher, JwtTokenGenerator tokenGen) CreateMocks()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TicketShieldDbContext(options);
        var hasher = new BcryptPasswordHasher();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "TicketShieldSuperSecretSecurityKeyForCapstoneProject2026",
                ["JwtSettings:Issuer"] = "TicketShield",
                ["JwtSettings:Audience"] = "TicketShieldApp",
                ["JwtSettings:ExpirationInMinutes"] = "60"
            })
            .Build();
        var tokenGen = new JwtTokenGenerator(config);

        return (db, hasher, tokenGen);
    }

    [Fact]
    public async Task Handle_WhenCredentialsAreValid_ShouldReturnToken()
    {
        // Arrange
        var (db, hasher, tokenGen) = CreateMocks();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "loginuser@ticketshield.vn",
            FullName = "Login User",
            PasswordHash = hasher.HashPassword("ValidPassword123!"),
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new LoginCommandHandler(db, hasher, tokenGen);
        var command = new LoginCommand
        {
            Email = "loginuser@ticketshield.vn",
            Password = "ValidPassword123!"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(user.Id, result.Data.UserId);
        Assert.False(string.IsNullOrEmpty(result.Data.Token));
    }

    [Fact]
    public async Task Handle_WhenPasswordIsIncorrect_ShouldThrowDomainException()
    {
        // Arrange
        var (db, hasher, tokenGen) = CreateMocks();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "wrongpass@ticketshield.vn",
            FullName = "Wrong Pass User",
            PasswordHash = hasher.HashPassword("CorrectPassword123!"),
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new LoginCommandHandler(db, hasher, tokenGen);
        var command = new LoginCommand
        {
            Email = "wrongpass@ticketshield.vn",
            Password = "IncorrectPassword999!"
        };

        // Act & Assert
        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenAccountIsInactive_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var (db, hasher, tokenGen) = CreateMocks();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "inactive@ticketshield.vn",
            FullName = "Inactive User",
            PasswordHash = hasher.HashPassword("Password123!"),
            Role = UserRole.User,
            IsActive = false // Locked
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new LoginCommandHandler(db, hasher, tokenGen);
        var command = new LoginCommand
        {
            Email = "inactive@ticketshield.vn",
            Password = "Password123!"
        };

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => handler.Handle(command, CancellationToken.None));
    }
}
