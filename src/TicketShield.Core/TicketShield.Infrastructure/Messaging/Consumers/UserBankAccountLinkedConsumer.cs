using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Contracts.Events;

namespace TicketShield.Infrastructure.Messaging.Consumers;

public class UserBankAccountLinkedConsumer : IConsumer<IUserBankAccountLinkedEvent>
{
    private readonly ITicketShieldDbContext _context;
    private readonly ILogger<UserBankAccountLinkedConsumer> _logger;

    public UserBankAccountLinkedConsumer(
        ITicketShieldDbContext context,
        ILogger<UserBankAccountLinkedConsumer> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IUserBankAccountLinkedEvent> context)
    {
        var message = context.Message;
        var seller = await _context.ShadowUsers
            .FirstOrDefaultAsync(user => user.Id == message.UserId, context.CancellationToken);

        if (seller == null)
        {
            _logger.LogWarning(
                "[EventBus] Không tìm thấy ShadowUser cho UserId: {UserId} để ghi số tài khoản nhận tiền",
                message.UserId);
            return;
        }

        seller.PayoutBankCode = message.BankCode;
        seller.PayoutAccountNumber = message.AccountNumber;
        seller.PayoutAccountName = message.AccountHolderName;
        seller.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation(
            "[EventBus] Đã ghi số tài khoản nhận tiền lên ShadowUser cho UserId: {UserId}",
            message.UserId);
    }
}
