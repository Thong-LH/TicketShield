using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using TicketShield.Contracts.Events;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Features.Auth.Commands.GoogleLogin;
using TicketShield.Identity.Domain.Entities;
using TicketShield.Identity.Domain.Enums;
using TicketShield.Identity.Infrastructure.Persistence;
using TicketShield.Identity.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Features.Auth;

public class GoogleLoginCommandHandlerTests
{
    private static (IdentityDbContext db, Mock<IGoogleAuthService> googleMock, JwtTokenGenerator tokenGen, Mock<IPublishEndpoint> publishMock) CreateMocks()
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
        var googleMock = new Mock<IGoogleAuthService>();
        var tokenGen = new JwtTokenGenerator(config);
        var publishMock = new Mock<IPublishEndpoint>();

        return (db, googleMock, tokenGen, publishMock);
    }

    [Fact]
    public async Task Handle_WhenGoogleUserIsNew_ShouldAutoRegisterAndPublishEventAndReturnTokens()
    {
        // Arrange
        var (db, googleMock, tokenGen, publishMock) = CreateMocks();
        googleMock.Setup(g => g.VerifyGoogleTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleUserInfo
            {
                GoogleId = "google-sub-123456",
                Email = "newgoogle@ticketshield.vn",
                Name = "New Google User"
            });

        var handler = new GoogleLoginCommandHandler(db, tokenGen, googleMock.Object, publishMock.Object);
        var command = new GoogleLoginCommand("valid-mock-token");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("newgoogle@ticketshield.vn", result.Data.User.Email);
        Assert.False(string.IsNullOrEmpty(result.Data.AccessToken));
        Assert.False(string.IsNullOrEmpty(result.Data.RefreshToken));

        var userInDb = await db.Users.FirstOrDefaultAsync(u => u.Email == "newgoogle@ticketshield.vn");
        Assert.NotNull(userInDb);
        Assert.Equal("google-sub-123456", userInDb.GoogleId);

        publishMock.Verify(p => p.Publish<IUserCreatedEvent>(
            It.Is<IUserCreatedEvent>(e => e.UserId == userInDb.Id && e.Email == userInDb.Email),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserAlreadyExists_ShouldLinkGoogleIdAndReturnTokensWithoutPublishingCreatedEvent()
    {
        // Arrange
        var (db, googleMock, tokenGen, publishMock) = CreateMocks();
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

        googleMock.Setup(g => g.VerifyGoogleTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleUserInfo
            {
                GoogleId = "google-sub-789012",
                Email = "existinguser@ticketshield.vn",
                Name = "Existing User"
            });

        var handler = new GoogleLoginCommandHandler(db, tokenGen, googleMock.Object, publishMock.Object);
        var command = new GoogleLoginCommand("valid-token-for-existing");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(existingUser.Id, result.Data.User.Id);

        var updatedUser = await db.Users.FirstOrDefaultAsync(u => u.Id == existingUser.Id);
        Assert.NotNull(updatedUser);
        Assert.Equal("google-sub-789012", updatedUser.GoogleId);

        publishMock.Verify(p => p.Publish<IUserCreatedEvent>(
            It.IsAny<IUserCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
