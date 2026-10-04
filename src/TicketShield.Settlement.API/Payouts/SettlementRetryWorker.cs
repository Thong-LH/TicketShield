using TicketShield.Settlement.API.Data;

namespace TicketShield.Settlement.API.Payouts;

public class SettlementRetryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;

    public SettlementRetryWorker(IServiceScopeFactory scopeFactory, TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _interval = interval ?? TimeSpan.FromSeconds(15);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var payouts = scope.ServiceProvider.GetRequiredService<SettlementPayoutService>();
            await payouts.RetryDueAsync(DateTimeOffset.UtcNow, stoppingToken);
            await payouts.RedeliverReportsAsync(stoppingToken);
        }
    }
}
