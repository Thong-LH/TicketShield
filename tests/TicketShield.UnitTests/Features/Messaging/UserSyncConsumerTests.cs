using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TicketShield.Contracts.Events;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Messaging.Consumers;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.Messaging;

public class UserSyncConsumerTests
{
    private static TicketShieldDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    [Fact]
    public async Task Consume_UserCreatedEvent_WhenUserDoesNotExist_ShouldInsertShadowUser()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var consumer = new UserCreatedConsumer(db, NullLogger<UserCreatedConsumer>.Instance);
        var userId = Guid.NewGuid();

        var eventMsg = new UserCreatedEvent(
            UserId: userId,
            Email: "new.sync.user@ticketshield.vn",
            FullName: "New Sync User",
            PhoneNumber: "0901112233",
            Role: "User",
            CreatedAt: DateTimeOffset.UtcNow
        );

        var contextMock = new Mock<ConsumeContext<IUserCreatedEvent>>();
        contextMock.Setup(c => c.Message).Returns(eventMsg);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        var shadowUser = await db.ShadowUsers.FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(shadowUser);
        Assert.Equal("new.sync.user@ticketshield.vn", shadowUser.Email);
        Assert.Equal("New Sync User", shadowUser.FullName);
        Assert.Equal("0901112233", shadowUser.PhoneNumber);
        Assert.Equal(UserRole.User, shadowUser.Role);
        Assert.True(shadowUser.IsActive);
    }

    [Fact]
    public async Task Consume_UserCreatedEvent_WhenUserAlreadyExists_ShouldBeIdempotentAndUpdate()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var initialUser = new ShadowUser
        {
            Id = userId,
            Email = "old.email@ticketshield.vn",
            FullName = "Old Name",
            Role = UserRole.User,
            IsActive = true
        };
        db.ShadowUsers.Add(initialUser);
        await db.SaveChangesAsync();

        var consumer = new UserCreatedConsumer(db, NullLogger<UserCreatedConsumer>.Instance);
        var eventMsg = new UserCreatedEvent(
            UserId: userId,
            Email: "updated.email@ticketshield.vn",
            FullName: "Updated Name",
            PhoneNumber: "0988776655",
            Role: "Organizer",
            CreatedAt: DateTimeOffset.UtcNow
        );

        var contextMock = new Mock<ConsumeContext<IUserCreatedEvent>>();
        contextMock.Setup(c => c.Message).Returns(eventMsg);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        var shadowUser = await db.ShadowUsers.FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(shadowUser);
        Assert.Equal("updated.email@ticketshield.vn", shadowUser.Email);
        Assert.Equal("Updated Name", shadowUser.FullName);
        Assert.Equal(UserRole.Organizer, shadowUser.Role);
        Assert.Equal(1, await db.ShadowUsers.CountAsync(u => u.Id == userId)); // Đảm bảo không duplicate
    }

    [Fact]
    public async Task Consume_UserProfileUpdatedEvent_ShouldUpdateExistingShadowUser()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var shadowUser = new ShadowUser
        {
            Id = userId,
            Email = "profile.user@ticketshield.vn",
            FullName = "Initial Profile Name",
            Role = UserRole.User,
            IsActive = true
        };
        db.ShadowUsers.Add(shadowUser);
        await db.SaveChangesAsync();

        var consumer = new UserProfileUpdatedConsumer(db, NullLogger<UserProfileUpdatedConsumer>.Instance);
        var eventMsg = new UserProfileUpdatedEvent(
            UserId: userId,
            FullName: "Updated Profile Name",
            PhoneNumber: "0911223344",
            Role: "Admin",
            IsActive: false,
            UpdatedAt: DateTimeOffset.UtcNow
        );

        var contextMock = new Mock<ConsumeContext<IUserProfileUpdatedEvent>>();
        contextMock.Setup(c => c.Message).Returns(eventMsg);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        var updated = await db.ShadowUsers.FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(updated);
        Assert.Equal("Updated Profile Name", updated.FullName);
        Assert.Equal("0911223344", updated.PhoneNumber);
        Assert.Equal(UserRole.Admin, updated.Role);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task Consume_UserProfileUpdatedEvent_WhenUserDoesNotExist_ShouldNotThrow()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var userId = Guid.NewGuid();

        var consumer = new UserProfileUpdatedConsumer(db, NullLogger<UserProfileUpdatedConsumer>.Instance);
        var eventMsg = new UserProfileUpdatedEvent(
            UserId: userId,
            FullName: "Ghost User",
            PhoneNumber: "0999999999",
            Role: "User",
            IsActive: true,
            UpdatedAt: DateTimeOffset.UtcNow
        );

        var contextMock = new Mock<ConsumeContext<IUserProfileUpdatedEvent>>();
        contextMock.Setup(c => c.Message).Returns(eventMsg);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act & Assert (Should not throw and nothing inserted)
        await consumer.Consume(contextMock.Object);

        var user = await db.ShadowUsers.FirstOrDefaultAsync(u => u.Id == userId);
        Assert.Null(user);
    }
}
