using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Infrastructure.Messaging;
using TicketShield.Infrastructure.Persistence;

namespace TicketShield.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=ticketshield_db;Username=postgres;Password=postgres";

        services.AddDbContext<TicketShieldDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<ITicketShieldDbContext>(provider =>
            provider.GetRequiredService<TicketShieldDbContext>());

        // Notification & Template Services (Sprint MF-03)
        services.AddSingleton<IEmailTemplateService, Services.EmailTemplateService>();
        services.AddScoped<IEmailService, Services.SmtpEmailService>();
        // Dynamic Resale Fee Services (BE-CORE-2.6.2)
        services.AddMemoryCache();
        services.AddScoped<ISystemSettingRepository, Persistence.Repositories.SystemSettingRepository>();
        services.AddScoped<IResaleFeeCalculator, Services.DynamicResaleFeeCalculator>();
        services.AddScoped<IEscrowBufferSettings, Services.EscrowBufferSettings>();

        // VietQR Service (BE-CORE-3.1.2)
        services.AddSingleton<IVietQrService, Services.VietQrService>();

        // Atomic escrow handoff between settlement and dispute (SCRUM-179)
        services.AddScoped<IEscrowSettlementCas, Services.EscrowSettlementCas>();

        services.AddScoped<IEscrowPayoutSettler, Services.EscrowPayoutSettler>();
        services.AddScoped<IPayoutReportApplier, Services.PayoutReportApplier>();
        services.AddSingleton<IDisputeEvidenceFileStore>(_ =>
        {
            var path = configuration["DisputeEvidence:StoragePath"];
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, "dispute-evidence");
            }

            return new Services.DisputeEvidenceFileStore(path);
        });
        services.AddHttpClient<ISettlementClient, Services.HttpSettlementClient>(client =>
        {
            var baseUrl = configuration["Settlement:BaseUrl"] ?? "http://localhost:5005";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        });
        services.AddHttpClient<IGateAccessLogClient, Services.HttpGateAccessLogClient>(client =>
        {
            var baseUrl = configuration["Organizer:BaseUrl"] ?? "http://localhost:5001";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        });
        services.AddSingleton<Workers.PayoutOutboxDispatcher>();
        services.AddHostedService(provider => provider.GetRequiredService<Workers.PayoutOutboxDispatcher>());
        services.AddSingleton<Workers.DisputeHarvestWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<Workers.DisputeHarvestWorker>());

        services.AddHostedService<Workers.AutomaticSettlementWorker>();
        services.AddHostedService<Workers.StuckSettlementWorker>();

        // Auto-Release Expired 10-Minute Listing Holds (BE-CORE-3.1.5 / BE-CORE-5.2.3)
        services.AddHostedService<Workers.ExpiredHoldReleaseWorker>();

        // Event Bus (RabbitMQ + MassTransit)
        services.AddEventBus(configuration);

        return services;
    }
}
