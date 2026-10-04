using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TicketShield.Contracts.Events;
using TicketShield.Domain.Entities;
using TicketShield.Infrastructure.Messaging.Consumers;
using TicketShield.Infrastructure.Persistence;

namespace TicketShield.UnitTests.Messaging;

public class UserBankAccountLinkedConsumerTests
{
    [Fact]
    public async Task Consume_WritesTheDefaultPayoutAccountOntoShadowUser()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        db.ShadowUsers.Add(new ShadowUser
        {
            Id = userId,
            Email = "seller@test.local",
            FullName = "Seller"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var consumer = new UserBankAccountLinkedConsumer(db, NullLogger<UserBankAccountLinkedConsumer>.Instance);
        await consumer.Consume(Context(new UserBankAccountLinkedEvent(userId, "MB", "0938434102", "SELLER")));

        var seller = await db.ShadowUsers.AsNoTracking().SingleAsync(user => user.Id == userId);
        Assert.Equal("MB", seller.PayoutBankCode);
        Assert.Equal("0938434102", seller.PayoutAccountNumber);
        Assert.Equal("SELLER", seller.PayoutAccountName);
    }

    [Fact]
    public async Task Consume_WhenShadowUserIsMissing_DoesNotThrow()
    {
        await using var db = CreateDb();
        var consumer = new UserBankAccountLinkedConsumer(db, NullLogger<UserBankAccountLinkedConsumer>.Instance);

        await consumer.Consume(Context(new UserBankAccountLinkedEvent(Guid.NewGuid(), "MB", "0938434102", "SELLER")));

        Assert.False(await db.ShadowUsers.AnyAsync());
    }

    private static ConsumeContext<IUserBankAccountLinkedEvent> Context(IUserBankAccountLinkedEvent message)
    {
        var context = new Mock<ConsumeContext<IUserBankAccountLinkedEvent>>();
        context.SetupGet(item => item.Message).Returns(message);
        context.SetupGet(item => item.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static TicketShieldDbContext CreateDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new TicketShieldDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
