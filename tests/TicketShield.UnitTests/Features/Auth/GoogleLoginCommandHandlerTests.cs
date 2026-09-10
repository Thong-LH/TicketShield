using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.Application.Features.Auth.Commands.GoogleLogin;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Features.Auth;

public class GoogleLoginCommandHandlerTests
{
    private static (TicketShieldDbContext db, GoogleAuthService googleService, JwtTokenGenerator tokenGen) CreateMocks()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TicketShieldDbContext(options);
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "TicketShieldSuperSecretSecurityKeyForCapstoneProject2026",
                ["JwtSettings:Issuer"] = "TicketShield",
                ["JwtSettings:Audience"] = "TicketShieldApp",
                ["JwtSettings:ExpirationInMinutes"] = "60",
                ["GoogleAuth:ClientId"] = "mock-google-client-id"
            })
            .Build();
        var googleService = new GoogleAuthService(config, NullLogger<GoogleAuthService>.Instance);
        var tokenGen = new JwtTokenGenerator(config);

        return (db, googleService, tokenGen);
    }

    [Fact]
    public async Task Handle_WhenGoogleUserIsNew_ShouldAutoRegisterAndReturnToken()
    {
        // Arrange
        var (db, googleService, tokenGen) = CreateMocks();
        var handler = new GoogleLoginCommandHandler(db, googleService, tokenGen);

        var command = new GoogleLoginCommand
        {
            IdToken = "mock-google-token-:newgoogle@ticketshield.vn:New Google User"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("newgoogle@ticketshield.vn", result.Data.Email);
        Assert.False(string.IsNullOrEmpty(result.Data.Token));

        var userInDb = await db.Users.FirstOrDefaultAsync(u => u.Email == "newgoogle@ticketshield.vn");
        Assert.NotNull(userInDb);
        Assert.False(string.IsNullOrEmpty(userInDb.GoogleId));
    }

    [Fact]
    public async Task Handle_WhenUserAlreadyExists_ShouldLinkGoogleIdAndReturnToken()
    {
        // Arrange
        var (db, googleService, tokenGen) = CreateMocks();
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "existinguser@ticketshield.vn",
            FullName = "Existing User",
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(existingUser);
        await db.SaveChangesAsync();

        var handler = new GoogleLoginCommandHandler(db, googleService, tokenGen);
        var command = new GoogleLoginCommand
        {
            IdToken = "mock-google-token-:existinguser@ticketshield.vn:Existing User"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(existingUser.Id, result.Data.UserId);

        var updatedUser = await db.Users.FirstOrDefaultAsync(u => u.Id == existingUser.Id);
        Assert.NotNull(updatedUser);
        Assert.False(string.IsNullOrEmpty(updatedUser.GoogleId));
    }
}
