using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.Application.Features.Auth.Commands.ForgotPassword;
using TicketShield.Application.Features.Auth.Commands.ResetPassword;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Features.Auth;

public class ForgotPasswordAndResetPasswordTests
{
    private static (TicketShieldDbContext db, BcryptPasswordHasher hasher, MockEmailService emailService) CreateMocks()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TicketShieldDbContext(options);
        var hasher = new BcryptPasswordHasher();
        var emailService = new MockEmailService(NullLogger<MockEmailService>.Instance);

        return (db, hasher, emailService);
    }

    [Fact]
    public async Task ForgotPassword_WhenEmailExists_ShouldGenerateOtpAndExpiry()
    {
        // Arrange
        var (db, _, emailService) = CreateMocks();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "forgotuser@ticketshield.vn",
            FullName = "Forgot User",
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new ForgotPasswordCommandHandler(db, emailService);
        var command = new ForgotPasswordCommand { Email = "forgotuser@ticketshield.vn" };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var updatedUser = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(updatedUser);
        Assert.NotNull(updatedUser.PasswordResetOtp);
        Assert.Equal(6, updatedUser.PasswordResetOtp.Length);
        Assert.True(updatedUser.PasswordResetOtpExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ResetPassword_WhenOtpIsValid_ShouldUpdatePasswordHashAndClearOtp()
    {
        // Arrange
        var (db, hasher, _) = CreateMocks();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "resetuser@ticketshield.vn",
            FullName = "Reset User",
            PasswordHash = hasher.HashPassword("OldPassword123!"),
            PasswordResetOtp = "654321",
            PasswordResetOtpExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new ResetPasswordCommandHandler(db, hasher);
        var command = new ResetPasswordCommand
        {
            Email = "resetuser@ticketshield.vn",
            Otp = "654321",
            NewPassword = "BrandNewPassword789!"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var updatedUser = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(updatedUser);
        Assert.Null(updatedUser.PasswordResetOtp);
        Assert.Null(updatedUser.PasswordResetOtpExpiresAt);
        Assert.True(hasher.VerifyPassword("BrandNewPassword789!", updatedUser.PasswordHash!));
    }

    [Fact]
    public async Task ResetPassword_WhenOtpIsExpired_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var (db, hasher, _) = CreateMocks();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "expireduser@ticketshield.vn",
            FullName = "Expired User",
            PasswordResetOtp = "112233",
            PasswordResetOtpExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5), // Expired 5 mins ago
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new ResetPasswordCommandHandler(db, hasher);
        var command = new ResetPasswordCommand
        {
            Email = "expireduser@ticketshield.vn",
            Otp = "112233",
            NewPassword = "BrandNewPassword789!"
        };

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => handler.Handle(command, CancellationToken.None));
    }
}
