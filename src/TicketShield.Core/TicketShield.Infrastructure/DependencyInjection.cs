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

        // Auth & Security Services
        services.AddSingleton<IPasswordHasher, Services.BcryptPasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, Services.JwtTokenGenerator>();
        services.AddScoped<IEmailService, Services.MockEmailService>();
        services.AddScoped<IGoogleAuthService, Services.GoogleAuthService>();

        // Dynamic Resale Fee Services (BE-CORE-2.6.2)
        services.AddMemoryCache();
        services.AddScoped<ISystemSettingRepository, Persistence.Repositories.SystemSettingRepository>();
        services.AddScoped<IResaleFeeCalculator, Services.DynamicResaleFeeCalculator>();

        // Event Bus (RabbitMQ + MassTransit)
        services.AddEventBus(configuration);

        return services;
    }
}
