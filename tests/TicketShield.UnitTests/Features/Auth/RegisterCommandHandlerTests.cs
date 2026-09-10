using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TicketShield.Application.Features.Auth.Commands.Register;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Features.Auth;

public class RegisterCommandHandlerTests
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
    public async Task Handle_WhenValidRequest_ShouldCreateUserAndReturnToken()
    {
        // Arrange
        var (db, hasher, tokenGen) = CreateMocks();
        var handler = new RegisterCommandHandler(db, hasher, tokenGen);
        var command = new RegisterCommand
        {
            Email = "newuser@ticketshield.vn",
            Password = "Password123!",
            FullName = "New User",
            PhoneNumber = "0900000000"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("newuser@ticketshield.vn", result.Data.Email);
        Assert.False(string.IsNullOrEmpty(result.Data.Token));

        var userInDb = await db.Users.FirstOrDefaultAsync(u => u.Email == "newuser@ticketshield.vn");
        Assert.NotNull(userInDb);
        Assert.True(hasher.VerifyPassword("Password123!", userInDb.PasswordHash!));
    }

    [Fact]
    public async Task Handle_WhenEmailAlreadyExists_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var (db, hasher, tokenGen) = CreateMocks();
        var handler = new RegisterCommandHandler(db, hasher, tokenGen);
        var command = new RegisterCommand
        {
            Email = "duplicate@ticketshield.vn",
            Password = "Password123!",
            FullName = "First User"
        };

        await handler.Handle(command, CancellationToken.None);

        // Act & Assert
        var duplicateCommand = new RegisterCommand
        {
            Email = "duplicate@ticketshield.vn",
            Password = "AnotherPassword456!",
            FullName = "Second User"
        };

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => handler.Handle(duplicateCommand, CancellationToken.None));
    }
}
