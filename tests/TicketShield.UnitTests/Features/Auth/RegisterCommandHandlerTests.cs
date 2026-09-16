using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using TicketShield.Contracts.Events;
using TicketShield.Identity.Application.Features.Auth.Commands.Register;
using TicketShield.Identity.Domain.Exceptions;
using TicketShield.Identity.Infrastructure.Persistence;
using TicketShield.Identity.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Features.Auth;

public class RegisterCommandHandlerTests
{
    private static (IdentityDbContext db, JwtTokenGenerator tokenGen, Mock<IPublishEndpoint> publishMock) CreateMocks()
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
        var publishMock = new Mock<IPublishEndpoint>();

        return (db, tokenGen, publishMock);
    }

    [Fact]
    public async Task Handle_WhenValidRequest_ShouldCreateUserAndPublishEventAndReturnTokens()
    {
        // Arrange
        var (db, tokenGen, publishMock) = CreateMocks();
        var handler = new RegisterCommandHandler(db, tokenGen, publishMock.Object);
        var command = new RegisterCommand(
            Email: "newuser@ticketshield.vn",
            Password: "Password123!",
            FullName: "New User",
            PhoneNumber: "0900000000"
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("newuser@ticketshield.vn", result.Data.User.Email);
        Assert.False(string.IsNullOrEmpty(result.Data.AccessToken));
        Assert.False(string.IsNullOrEmpty(result.Data.RefreshToken));
        Assert.Equal(900, result.Data.ExpiresIn);

        var userInDb = await db.Users.FirstOrDefaultAsync(u => u.Email == "newuser@ticketshield.vn");
        Assert.NotNull(userInDb);
        Assert.True(BCrypt.Net.BCrypt.Verify("Password123!", userInDb.PasswordHash!));

        var refreshTokenInDb = await db.RefreshTokens.FirstOrDefaultAsync(r => r.UserId == userInDb.Id);
        Assert.NotNull(refreshTokenInDb);
        Assert.True(refreshTokenInDb.IsActive);

        // Verify that UserCreatedEvent was published to Outbox
        publishMock.Verify(p => p.Publish<IUserCreatedEvent>(
            It.Is<IUserCreatedEvent>(e => e.UserId == userInDb.Id && e.Email == userInDb.Email),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmailAlreadyExists_ShouldThrowBadRequestException()
    {
        // Arrange
        var (db, tokenGen, publishMock) = CreateMocks();
        var handler = new RegisterCommandHandler(db, tokenGen, publishMock.Object);
        var command = new RegisterCommand(
            Email: "duplicate@ticketshield.vn",
            Password: "Password123!",
            FullName: "First User"
        );

        await handler.Handle(command, CancellationToken.None);

        // Act & Assert
        var duplicateCommand = new RegisterCommand(
            Email: "duplicate@ticketshield.vn",
            Password: "AnotherPassword456!",
            FullName: "Second User"
        );

        await Assert.ThrowsAsync<BadRequestException>(
            () => handler.Handle(duplicateCommand, CancellationToken.None));
    }
}
