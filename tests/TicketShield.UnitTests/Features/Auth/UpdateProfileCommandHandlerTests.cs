using FluentValidation.TestHelper;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using TicketShield.Contracts.Events;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Features.Auth.Commands.UpdateProfile;
using TicketShield.Identity.Domain.Entities;
using TicketShield.Identity.Domain.Enums;
using TicketShield.Identity.Domain.Exceptions;
using TicketShield.Identity.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.Auth;

public class UpdateProfileCommandHandlerTests
{
    private static (IdentityDbContext db, Mock<ICurrentUserService> currentUserMock, Mock<IPublishEndpoint> publishMock) CreateMocks(Guid? currentUserId = null)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new IdentityDbContext(options);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.UserId).Returns(currentUserId);
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(currentUserId.HasValue);

        var publishMock = new Mock<IPublishEndpoint>();

        return (db, currentUserMock, publishMock);
    }

    [Fact]
    public async Task Handle_WhenValidRequest_ShouldUpdateUserProfileAndPublishEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var (db, currentUserMock, publishMock) = CreateMocks(userId);

        var initialUser = new User
        {
            Id = userId,
            Email = "john.doe@ticketshield.vn",
            FullName = "John Doe",
            PhoneNumber = "0901234567",
            IdCardNumber = "001234567890",
            Role = UserRole.User,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        db.Users.Add(initialUser);
        await db.SaveChangesAsync();

        var handler = new UpdateProfileCommandHandler(db, currentUserMock.Object, publishMock.Object);
        var command = new UpdateProfileCommand(
            FullName: "Johnathan Doe Updated",
            PhoneNumber: "0987654321",
            IdCardNumber: "009876543210"
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("Johnathan Doe Updated", result.Data.FullName);
        Assert.Equal("0987654321", result.Data.PhoneNumber);
        Assert.Equal("009876543210", result.Data.IdCardNumber);
        Assert.Equal("john.doe@ticketshield.vn", result.Data.Email);

        // Verify DB update
        var updatedInDb = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(updatedInDb);
        Assert.Equal("Johnathan Doe Updated", updatedInDb.FullName);
        Assert.Equal("0987654321", updatedInDb.PhoneNumber);
        Assert.Equal("009876543210", updatedInDb.IdCardNumber);

        // Verify IUserProfileUpdatedEvent published via Outbox
        publishMock.Verify(p => p.Publish<IUserProfileUpdatedEvent>(
            It.Is<IUserProfileUpdatedEvent>(e =>
                e.UserId == userId &&
                e.FullName == "Johnathan Doe Updated" &&
                e.PhoneNumber == "0987654321" &&
                e.Role == "User" &&
                e.IsActive == true &&
                e.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-1)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var (db, currentUserMock, publishMock) = CreateMocks(null); // No logged in user
        var handler = new UpdateProfileCommandHandler(db, currentUserMock.Object, publishMock.Object);
        var command = new UpdateProfileCommand("Some Name");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var nonExistentUserId = Guid.NewGuid();
        var (db, currentUserMock, publishMock) = CreateMocks(nonExistentUserId);
        var handler = new UpdateProfileCommandHandler(db, currentUserMock.Object, publishMock.Object);
        var command = new UpdateProfileCommand("Some Name");

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenUserIsInactive_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var (db, currentUserMock, publishMock) = CreateMocks(userId);

        var inactiveUser = new User
        {
            Id = userId,
            Email = "inactive@ticketshield.vn",
            FullName = "Inactive User",
            Role = UserRole.User,
            IsActive = false
        };
        db.Users.Add(inactiveUser);
        await db.SaveChangesAsync();

        var handler = new UpdateProfileCommandHandler(db, currentUserMock.Object, publishMock.Object);
        var command = new UpdateProfileCommand("New Name");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validator_WhenFullNameEmpty_ShouldHaveValidationError(string? invalidFullName)
    {
        var validator = new UpdateProfileCommandValidator();
        var command = new UpdateProfileCommand(invalidFullName!);

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.FullName);
    }

    [Fact]
    public void Validator_WhenFullNameTooLong_ShouldHaveValidationError()
    {
        var validator = new UpdateProfileCommandValidator();
        var command = new UpdateProfileCommand(new string('a', 101));

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.FullName);
    }

    [Theory]
    [InlineData("invalid-phone-abc!@#")]
    [InlineData("phone123")]
    public void Validator_WhenPhoneNumberInvalid_ShouldHaveValidationError(string invalidPhone)
    {
        var validator = new UpdateProfileCommandValidator();
        var command = new UpdateProfileCommand("Valid Name", PhoneNumber: invalidPhone);

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void Validator_WhenValidInput_ShouldNotHaveValidationError()
    {
        var validator = new UpdateProfileCommandValidator();
        var command = new UpdateProfileCommand("Valid Name", "0901234567", "001234567890");

        var result = validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
