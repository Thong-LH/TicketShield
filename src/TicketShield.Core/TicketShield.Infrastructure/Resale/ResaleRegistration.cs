using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TicketShield.Contracts.Organizer.V1;

namespace TicketShield.Infrastructure.Resale;

public static class ResaleRegistration
{
    public static bool AddCoreResale(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var o = configuration.GetSection("OrganizerGrpc").Get<OrganizerConnectionOptions>() ?? new();
        if (!o.Enabled) return false;
        if (!Uri.TryCreate(o.Address, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "https" || environment.IsDevelopment() && uri.Scheme == "http" && uri.IsLoopback) ||
            !string.IsNullOrEmpty(uri.UserInfo) || o.ApiKey.Length < 32 || o.HmacKey.Length < 32 ||
            !Guid.TryParseExact(o.OrganizerId, "D", out _) || o.DeadlineSeconds is < 1 or > 120)
            throw new InvalidOperationException("OrganizerGrpc requires TLS (or Development loopback), identity, keys (32+ characters) and a bounded deadline.");
        services.AddSingleton(o);
        services.TryAddSingleton(TimeProvider.System);
        services.AddGrpcClient<OrganizerResaleService.OrganizerResaleServiceClient>(c => c.Address = uri);
        services.AddScoped<OrganizerGateway>();
        services.AddScoped<TicketResaleWorkflow>();
        services.AddDbContext<CoreResaleStore>(b => b.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
            pg => pg.MigrationsHistoryTable("__CoreResaleMigrationsHistory")));
        services.AddHostedService<ResaleRecoveryWorker>();
        return true;
    }
}

public sealed class ResaleRecoveryWorker(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            using var scope = scopes.CreateScope();
            try { await scope.ServiceProvider.GetRequiredService<TicketResaleWorkflow>().RecoverPending(stoppingToken); }
            catch (Exception) when (!stoppingToken.IsCancellationRequested) { /* Durable state retains the next retry. Never log secret-bearing payloads. */ }
        }
    }
}
