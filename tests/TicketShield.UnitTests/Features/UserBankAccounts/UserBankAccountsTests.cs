using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using TicketShield.Contracts.Events;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Features.UserBankAccounts.Commands.CreateUserBankAccount;
using TicketShield.Identity.Application.Features.UserBankAccounts.Commands.DeleteUserBankAccount;
using TicketShield.Identity.Application.Features.UserBankAccounts.Commands.SetDefaultUserBankAccount;
using TicketShield.Identity.Application.Features.UserBankAccounts.Queries.GetUserBankAccounts;
using TicketShield.Identity.Domain.Entities;
using TicketShield.Identity.Domain.Exceptions;
using TicketShield.Identity.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.UserBankAccounts;

public class UserBankAccountsTests
{
    private static (IdentityDbContext dbContext, User testUser) CreateInMemoryIdentityDbContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new IdentityDbContext(options);
        var testUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "seller.bank@ticketshield.vn",
            FullName = "Nguyen Van Seller Bank",
            PasswordHash = "hashed_pass"
        };
        context.Users.Add(testUser);
        context.SaveChanges();

        return (context, testUser);
    }

    private class MockCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; }
        public string? Email => "seller.bank@ticketshield.vn";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;

        public MockCurrentUserService(Guid? userId) => UserId = userId;
    }

    private static CreateUserBankAccountCommandHandler CreateHandler(
        IdentityDbContext dbContext,
        MockCurrentUserService currentUserService,
        Mock<IPublishEndpoint>? publish = null)
    {
        if (publish == null)
        {
            publish = new Mock<IPublishEndpoint>();
            publish
                .Setup(endpoint => endpoint.Publish<IUserBankAccountLinkedEvent>(
                    It.IsAny<IUserBankAccountLinkedEvent>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        return new CreateUserBankAccountCommandHandler(dbContext, currentUserService, publish.Object);
    }

    [Fact]
    public async Task CreateUserBankAccount_FirstAccount_ShouldSetIsDefaultTrue()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var handler = CreateHandler(dbContext, currentUserService);

        var command = new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK", isDefault: false);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.Data.IsDefault); // Forced to true for first account
        Assert.Equal("MB", result.Data.BankCode);
        Assert.Equal("0938434102", result.Data.BankAccountNumber);
        Assert.Equal("NGUYEN VAN SELLER BANK", result.Data.AccountHolderName);
    }

    [Fact]
    public async Task CreateUserBankAccount_SecondAccountSetDefault_ShouldUnsetDefaultFromFirstAccount()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var handler = CreateHandler(dbContext, currentUserService);

        var firstAccountCommand = new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK", isDefault: true);
        await handler.Handle(firstAccountCommand, CancellationToken.None);

        var secondAccountCommand = new CreateUserBankAccountCommand("VCB", "10987654321", "NGUYEN VAN SELLER BANK", isDefault: true);

        // Act
        var result = await handler.Handle(secondAccountCommand, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.Data.IsDefault);

        var accounts = await dbContext.UserBankAccounts.Where(b => b.UserId == testUser.Id).ToListAsync();
        Assert.Equal(2, accounts.Count);

        var firstAccInDb = accounts.First(b => b.BankAccountNumber == "0938434102");
        var secondAccInDb = accounts.First(b => b.BankAccountNumber == "10987654321");

        Assert.False(firstAccInDb.IsDefault);
        Assert.True(secondAccInDb.IsDefault);
    }

    [Fact]
    public async Task GetUserBankAccounts_ShouldReturnUserAccounts()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var createHandler = CreateHandler(dbContext, currentUserService);
        var getHandler = new GetUserBankAccountsQueryHandler(dbContext, currentUserService);

        await createHandler.Handle(new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK"), CancellationToken.None);
        await createHandler.Handle(new CreateUserBankAccountCommand("TCB", "1900000001", "NGUYEN VAN SELLER BANK", isDefault: false), CancellationToken.None);

        // Act
        var result = await getHandler.Handle(new GetUserBankAccountsQuery(), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(2, result.Data.Count);
        Assert.Contains(result.Data, a => a.IsDefault && a.BankCode == "MB");
    }

    [Fact]
    public async Task CreateUserBankAccount_WhenUserNotAuthenticated_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var (dbContext, _) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(null); // Anonymous
        var handler = CreateHandler(dbContext, currentUserService);

        var command = new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CreateUserBankAccount_WhenAddingNonDefault_PublishesTheCurrentDefaultAccount()
    {
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        IUserBankAccountLinkedEvent? published = null;
        var publish = new Mock<IPublishEndpoint>();
        publish
            .Setup(endpoint => endpoint.Publish<IUserBankAccountLinkedEvent>(
                It.IsAny<IUserBankAccountLinkedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<IUserBankAccountLinkedEvent, CancellationToken>((message, _) => published = message)
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(dbContext, currentUserService, publish);

        await handler.Handle(new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK"), CancellationToken.None);
        await handler.Handle(new CreateUserBankAccountCommand("TCB", "1900000001", "NGUYEN VAN SELLER BANK", isDefault: false), CancellationToken.None);

        Assert.NotNull(published);
        Assert.Equal(testUser.Id, published.UserId);
        Assert.Equal("MB", published.BankCode);
        Assert.Equal("0938434102", published.AccountNumber);
    }

    private static DeleteUserBankAccountCommandHandler CreateDeleteHandler(
        IdentityDbContext dbContext,
        MockCurrentUserService currentUserService,
        Mock<IPublishEndpoint>? publish = null)
    {
        publish ??= new Mock<IPublishEndpoint>();
        return new DeleteUserBankAccountCommandHandler(dbContext, currentUserService, publish.Object);
    }

    private static SetDefaultUserBankAccountCommandHandler CreateSetDefaultHandler(
        IdentityDbContext dbContext,
        MockCurrentUserService currentUserService,
        Mock<IPublishEndpoint>? publish = null)
    {
        publish ??= new Mock<IPublishEndpoint>();
        return new SetDefaultUserBankAccountCommandHandler(dbContext, currentUserService, publish.Object);
    }

    [Fact]
    public async Task CreateUserBankAccount_DuplicateAccount_ShouldThrowBadRequestException()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var handler = CreateHandler(dbContext, currentUserService);

        await handler.Handle(new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK"), CancellationToken.None);

        // Act & Assert (Same BankCode & AccountNumber with case & whitespace variations)
        var duplicateCommand = new CreateUserBankAccountCommand(" mb ", " 0938434102 ", "NGUYEN VAN SELLER BANK");
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(duplicateCommand, CancellationToken.None));
        Assert.Contains("already been linked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateUserBankAccount_ExceedMaximumLimitOf5_ShouldThrowBadRequestException()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var handler = CreateHandler(dbContext, currentUserService);

        for (int i = 1; i <= 5; i++)
        {
            await handler.Handle(new CreateUserBankAccountCommand($"BANK{i}", $"ACC{i:D6}", "NGUYEN VAN SELLER BANK"), CancellationToken.None);
        }

        // Act & Assert (6th account attempt)
        var sixthCommand = new CreateUserBankAccountCommand("BANK6", "ACC000006", "NGUYEN VAN SELLER BANK");
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(sixthCommand, CancellationToken.None));
        Assert.Contains("up to 5", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteUserBankAccount_WhenNotAuthenticated_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var (dbContext, _) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(null);
        var handler = CreateDeleteHandler(dbContext, currentUserService);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(new DeleteUserBankAccountCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteUserBankAccount_NonExistentAccount_ShouldThrowNotFoundException()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var handler = CreateDeleteHandler(dbContext, currentUserService);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new DeleteUserBankAccountCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteUserBankAccount_OnlyDefaultAccountRemaining_ShouldThrowBadRequestException()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var createHandler = CreateHandler(dbContext, currentUserService);
        var deleteHandler = CreateDeleteHandler(dbContext, currentUserService);

        var created = await createHandler.Handle(new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK"), CancellationToken.None);

        // Act & Assert (Cannot delete the only default bank account)
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => deleteHandler.Handle(new DeleteUserBankAccountCommand(created.Data.Id), CancellationToken.None));
        Assert.Contains("only default payout account", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteUserBankAccount_NonDefaultAccount_ShouldDeleteSuccessfullyWithoutPublishingEvent()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var createHandler = CreateHandler(dbContext, currentUserService);

        var publishMock = new Mock<IPublishEndpoint>();
        var deleteHandler = CreateDeleteHandler(dbContext, currentUserService, publishMock);

        var defaultAcc = await createHandler.Handle(new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK"), CancellationToken.None);
        var nonDefaultAcc = await createHandler.Handle(new CreateUserBankAccountCommand("VCB", "1011121314", "NGUYEN VAN SELLER BANK", isDefault: false), CancellationToken.None);

        publishMock.Invocations.Clear();

        // Act
        var result = await deleteHandler.Handle(new DeleteUserBankAccountCommand(nonDefaultAcc.Data.Id), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);

        var remainingAccounts = await dbContext.UserBankAccounts.Where(b => b.UserId == testUser.Id).ToListAsync();
        Assert.Single(remainingAccounts);
        Assert.Equal(defaultAcc.Data.Id, remainingAccounts[0].Id);
        Assert.True(remainingAccounts[0].IsDefault);

        // Non-default deletion should not trigger IUserBankAccountLinkedEvent
        publishMock.Verify(p => p.Publish<IUserBankAccountLinkedEvent>(It.IsAny<IUserBankAccountLinkedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteUserBankAccount_DefaultAccountWithRemainingAccounts_ShouldElevateOldestToDefaultAndPublishEvent()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var createHandler = CreateHandler(dbContext, currentUserService);

        var publishMock = new Mock<IPublishEndpoint>();
        IUserBankAccountLinkedEvent? publishedEvent = null;
        publishMock
            .Setup(p => p.Publish<IUserBankAccountLinkedEvent>(It.IsAny<IUserBankAccountLinkedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IUserBankAccountLinkedEvent, CancellationToken>((msg, _) => publishedEvent = msg)
            .Returns(Task.CompletedTask);

        var deleteHandler = CreateDeleteHandler(dbContext, currentUserService, publishMock);

        var defaultAcc = await createHandler.Handle(new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK"), CancellationToken.None);
        var secondAcc = await createHandler.Handle(new CreateUserBankAccountCommand("VCB", "1011121314", "NGUYEN VAN SELLER BANK", isDefault: false), CancellationToken.None);
        var thirdAcc = await createHandler.Handle(new CreateUserBankAccountCommand("TCB", "1900000001", "NGUYEN VAN SELLER BANK", isDefault: false), CancellationToken.None);

        publishMock.Invocations.Clear();
        publishedEvent = null;

        // Act: Delete default account (MB)
        var result = await deleteHandler.Handle(new DeleteUserBankAccountCommand(defaultAcc.Data.Id), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);

        var remainingAccounts = await dbContext.UserBankAccounts.Where(b => b.UserId == testUser.Id).OrderBy(b => b.CreatedAt).ToListAsync();
        Assert.Equal(2, remainingAccounts.Count);

        // Second account (VCB, oldest remaining) should be elevated to default
        var newDefault = remainingAccounts.First(b => b.Id == secondAcc.Data.Id);
        Assert.True(newDefault.IsDefault);
        Assert.Equal("VCB", newDefault.BankCode);

        // Third account should remain non-default
        var thirdInDb = remainingAccounts.First(b => b.Id == thirdAcc.Data.Id);
        Assert.False(thirdInDb.IsDefault);

        // Event should be published with new default details (VCB)
        Assert.NotNull(publishedEvent);
        Assert.Equal(testUser.Id, publishedEvent.UserId);
        Assert.Equal("VCB", publishedEvent.BankCode);
        Assert.Equal("1011121314", publishedEvent.AccountNumber);
    }

    [Fact]
    public async Task SetDefaultUserBankAccount_WhenNotAuthenticated_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var (dbContext, _) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(null);
        var handler = CreateSetDefaultHandler(dbContext, currentUserService);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(new SetDefaultUserBankAccountCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task SetDefaultUserBankAccount_NonExistentAccount_ShouldThrowNotFoundException()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var handler = CreateSetDefaultHandler(dbContext, currentUserService);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new SetDefaultUserBankAccountCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task SetDefaultUserBankAccount_AlreadyDefault_ShouldBeIdempotentWithoutRePublishingEvent()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var createHandler = CreateHandler(dbContext, currentUserService);

        var publishMock = new Mock<IPublishEndpoint>();
        var setDefaultHandler = CreateSetDefaultHandler(dbContext, currentUserService, publishMock);

        var defaultAcc = await createHandler.Handle(new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK"), CancellationToken.None);

        publishMock.Invocations.Clear();

        // Act (Set default on an account that is already default)
        var result = await setDefaultHandler.Handle(new SetDefaultUserBankAccountCommand(defaultAcc.Data.Id), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.Data.IsDefault);

        // Idempotent: Should not publish new event
        publishMock.Verify(p => p.Publish<IUserBankAccountLinkedEvent>(It.IsAny<IUserBankAccountLinkedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetDefaultUserBankAccount_ValidDifferentAccount_ShouldSetNewDefaultAndPublishEvent()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var createHandler = CreateHandler(dbContext, currentUserService);

        var publishMock = new Mock<IPublishEndpoint>();
        IUserBankAccountLinkedEvent? publishedEvent = null;
        publishMock
            .Setup(p => p.Publish<IUserBankAccountLinkedEvent>(It.IsAny<IUserBankAccountLinkedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IUserBankAccountLinkedEvent, CancellationToken>((msg, _) => publishedEvent = msg)
            .Returns(Task.CompletedTask);

        var setDefaultHandler = CreateSetDefaultHandler(dbContext, currentUserService, publishMock);

        var firstAcc = await createHandler.Handle(new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK"), CancellationToken.None);
        var secondAcc = await createHandler.Handle(new CreateUserBankAccountCommand("VCB", "1011121314", "NGUYEN VAN SELLER BANK", isDefault: false), CancellationToken.None);

        publishMock.Invocations.Clear();
        publishedEvent = null;

        // Act: Set second account as default
        var result = await setDefaultHandler.Handle(new SetDefaultUserBankAccountCommand(secondAcc.Data.Id), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.Data.IsDefault);
        Assert.Equal(secondAcc.Data.Id, result.Data.Id);

        var accountsInDb = await dbContext.UserBankAccounts.Where(b => b.UserId == testUser.Id).ToListAsync();
        var firstInDb = accountsInDb.First(b => b.Id == firstAcc.Data.Id);
        var secondInDb = accountsInDb.First(b => b.Id == secondAcc.Data.Id);

        Assert.False(firstInDb.IsDefault);
        Assert.True(secondInDb.IsDefault);

        // Event should be published with second account details
        Assert.NotNull(publishedEvent);
        Assert.Equal(testUser.Id, publishedEvent.UserId);
        Assert.Equal("VCB", publishedEvent.BankCode);
        Assert.Equal("1011121314", publishedEvent.AccountNumber);
    }
}
