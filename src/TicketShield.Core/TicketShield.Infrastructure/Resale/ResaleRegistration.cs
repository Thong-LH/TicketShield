using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Contracts.Organizer.V1;

namespace TicketShield.Infrastructure.Resale;

public static class ResaleRegistration
{
    public static bool AddCoreResale(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection("OrganizerGrpc").Get<OrganizerConnectionOptions>() ?? new();
        if (!options.Enabled)
        {
            return false;
        }

        if (!Uri.TryCreate(options.Address, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "https" || environment.IsDevelopment() && uri.Scheme == "http" && uri.IsLoopback) ||
            !string.IsNullOrEmpty(uri.UserInfo) || options.ApiKey.Length < 32 || options.HmacKey.Length < 32 ||
            !Guid.TryParseExact(options.OrganizerId, "D", out _) || options.DeadlineSeconds is < 1 or > 120)
        {
            throw new InvalidOperationException("OrganizerGrpc requires TLS (or Development loopback), identity, keys (32+ characters) and a bounded deadline.");
        }

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddGrpcClient<OrganizerResaleService.OrganizerResaleServiceClient>(c => c.Address = uri);
        services.AddScoped<OrganizerGateway>();

        // Đăng ký Service qua Interface Application Layer theo chuẩn Clean Architecture
        services.AddScoped<ITicketVerificationService, TicketVerificationService>();
        services.AddScoped<ITicketResaleWorkflow, TicketVerificationService>();
        services.AddScoped<TicketVerificationService>();

        services.AddDbContext<CoreResaleStore>(b => b.UseNpgsql(
            configuration.GetConnectionString("DefaultConnection"),
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
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopes.CreateScope();
            try
            {
                var service = scope.ServiceProvider.GetRequiredService<ITicketVerificationService>();
                await service.RecoverPending(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                // Durable state retains the next retry. Never log secret-bearing payloads.
            }
        }
    }
}
