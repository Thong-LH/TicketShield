using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Features.UserBankAccounts.Commands.CreateUserBankAccount;
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

    [Fact]
    public async Task CreateUserBankAccount_FirstAccount_ShouldSetIsDefaultTrue()
    {
        // Arrange
        var (dbContext, testUser) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(testUser.Id);
        var handler = new CreateUserBankAccountCommandHandler(dbContext, currentUserService);

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
        var handler = new CreateUserBankAccountCommandHandler(dbContext, currentUserService);

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
        var createHandler = new CreateUserBankAccountCommandHandler(dbContext, currentUserService);
        var getHandler = new GetUserBankAccountsQueryHandler(dbContext, currentUserService);

        await createHandler.Handle(new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK"), CancellationToken.None);
        await createHandler.Handle(new CreateUserBankAccountCommand("TCB", "1900000001", "NGUYEN VAN SELLER BANK", isDefault: false), CancellationToken.None);

        // Act
        var result = await getHandler.Handle(new GetUserBankAccountsQuery(), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(2, result.Data.Count);
        Assert.True(result.Data[0].IsDefault); // Default account comes first
        Assert.Equal("MB", result.Data[0].BankCode);
    }

    [Fact]
    public async Task CreateUserBankAccount_WhenUserNotAuthenticated_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var (dbContext, _) = CreateInMemoryIdentityDbContext();
        var currentUserService = new MockCurrentUserService(null); // Anonymous
        var handler = new CreateUserBankAccountCommandHandler(dbContext, currentUserService);

        var command = new CreateUserBankAccountCommand("MB", "0938434102", "NGUYEN VAN SELLER BANK");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(command, CancellationToken.None));
    }
}
