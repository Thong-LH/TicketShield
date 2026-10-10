using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.EscrowBuffer.Commands.UpdateEscrowBuffer;
using TicketShield.Application.Features.Admin.EscrowBuffer.Queries.GetEscrowBuffer;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Persistence.Repositories;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Features;

public class EscrowBufferAdminTests
{
    [Fact]
    public async Task Get_WhenUnset_ReturnsDefaults()
    {
        await using var context = NewContext();
        var query = new GetEscrowBufferQueryHandler(Settings(context));

        var result = await query.Handle(new GetEscrowBufferQuery(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(86400, result.Data!.BufferSeconds);
        Assert.Equal(7200, result.Data.CutoffSeconds);
    }

    [Fact]
    public async Task Get_WhenStored_ReturnsStoredSeconds()
    {
        await using var context = NewContext();
        context.SystemSettings.AddRange(
            Setting("EscrowBuffer_SettlementSeconds", "30"),
            Setting("EscrowBuffer_CutoffSeconds", "7200"));
        await context.SaveChangesAsync();
        var query = new GetEscrowBufferQueryHandler(Settings(context));

        var result = await query.Handle(new GetEscrowBufferQuery(), CancellationToken.None);

        Assert.Equal(30, result.Data!.BufferSeconds);
        Assert.Equal(7200, result.Data.CutoffSeconds);
    }

    [Fact]
    public async Task Put_ValidSeconds_ThenGetReturnsThem()
    {
        await using var context = NewContext();
        var settings = Settings(context);
        var update = Handler(settings);
        var query = new GetEscrowBufferQueryHandler(settings);

        var saved = await update.Handle(new UpdateEscrowBufferCommand
        {
            BufferSeconds = 30,
            CutoffSeconds = 7200
        }, CancellationToken.None);
        var read = await query.Handle(new GetEscrowBufferQuery(), CancellationToken.None);

        Assert.True(saved.Success);
        Assert.Equal(30, read.Data!.BufferSeconds);
        Assert.Equal(7200, read.Data.CutoffSeconds);
    }

    [Fact]
    public async Task Put_Zero_IsRejected_AndStoredValueStays()
    {
        await using var context = NewContext();
        var settings = Settings(context);
        await Handler(settings).Handle(new UpdateEscrowBufferCommand
        {
            BufferSeconds = 30,
            CutoffSeconds = 7200
        }, CancellationToken.None);

        var validation = await new UpdateEscrowBufferCommandValidator().ValidateAsync(new UpdateEscrowBufferCommand
        {
            BufferSeconds = 0,
            CutoffSeconds = 7200
        });
        var read = await new GetEscrowBufferQueryHandler(settings).Handle(new GetEscrowBufferQuery(), CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Equal(30, read.Data!.BufferSeconds);
        Assert.Equal(7200, read.Data.CutoffSeconds);
    }

    [Fact]
    public async Task Put_DoesNotRewriteLockedEscrowUnlockAt()
    {
        await using var context = NewContext();
        var unlockAt = new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);
        var escrow = new EscrowTransaction
        {
            ListingId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = EscrowStatus.Locked,
            UnlockAt = unlockAt
        };
        context.EscrowTransactions.Add(escrow);
        await context.SaveChangesAsync();

        await Handler(Settings(context)).Handle(new UpdateEscrowBufferCommand
        {
            BufferSeconds = 30,
            CutoffSeconds = 7200
        }, CancellationToken.None);

        var stored = await context.EscrowTransactions.FindAsync(escrow.Id);
        Assert.Equal(unlockAt, stored!.UnlockAt);
        Assert.Equal(EscrowStatus.Locked, stored.Status);
    }

    private static TicketShieldDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    private static EscrowBufferSettings Settings(TicketShieldDbContext context)
    {
        return new EscrowBufferSettings(
            new SystemSettingRepository(context),
            new MemoryCache(new MemoryCacheOptions()));
    }

    private static UpdateEscrowBufferCommandHandler Handler(EscrowBufferSettings settings)
    {
        var user = new Mock<ICurrentUserService>();
        user.Setup(s => s.UserId).Returns(Guid.NewGuid());
        return new UpdateEscrowBufferCommandHandler(settings, user.Object);
    }

    private static SystemSetting Setting(string key, string value)
    {
        return new SystemSetting
        {
            SettingKey = key,
            SettingValue = value,
            DataType = "Integer"
        };
    }
}
