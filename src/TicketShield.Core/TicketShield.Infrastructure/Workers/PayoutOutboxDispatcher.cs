using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;

namespace TicketShield.Infrastructure.Workers;

public class PayoutOutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PayoutOutboxDispatcher> _logger;
    private readonly TimeSpan _interval;

    public PayoutOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<PayoutOutboxDispatcher> logger,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(15);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Payout outbox dispatch failed.");
            }
        }
    }

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ITicketShieldDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<ISettlementClient>();
        var pending = await db.OutboxMessages
            .Where(row => row.EventType == nameof(PayoutRequestedEvent) && row.ProcessedAt == null)
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var message in pending)
        {
            var command = JsonSerializer.Deserialize<PayoutRequestedEvent>(message.Payload);
            if (command == null)
            {
                continue;
            }

            if (!await client.SendAsync(command, cancellationToken))
            {
                continue;
            }

            message.ProcessedAt = DateTimeOffset.UtcNow;
            message.UpdatedAt = message.ProcessedAt.Value;
            sent++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return sent;
    }
}
