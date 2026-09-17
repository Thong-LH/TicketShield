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
        // Dynamic Resale Fee Services (BE-CORE-2.6.2)
        services.AddMemoryCache();
        services.AddScoped<ISystemSettingRepository, Persistence.Repositories.SystemSettingRepository>();
        services.AddScoped<IResaleFeeCalculator, Services.DynamicResaleFeeCalculator>();

        // VietQR Service (BE-CORE-3.1.2)
        services.AddSingleton<IVietQrService, Services.VietQrService>();

        // Auto-Release Expired Hold Worker (BE-CORE-3.1.3 / BR-E01)
        services.AddHostedService<Workers.ExpiredHoldReleaseWorker>();

        // Event Bus (RabbitMQ + MassTransit)
        services.AddEventBus(configuration);

        return services;
    }
}
