using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Infrastructure.Persistence;
using TicketShield.Identity.Infrastructure.Services;

namespace TicketShield.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database Connection (Database-per-service: identity_db)
        var connectionString = configuration.GetConnectionString("IdentityConnection")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=identity_db;Username=postgres;Password=12345";

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName);
                npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            }));

        services.AddScoped<IIdentityDbContext>(provider => provider.GetRequiredService<IdentityDbContext>());

        // MassTransit + Transactional Outbox
        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<IdentityDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                o.DuplicateDetectionWindow = TimeSpan.FromSeconds(30);
            });

            var useInMemory = configuration.GetValue<bool>("RabbitMQ:UseInMemory", false);
            if (useInMemory)
            {
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.ConfigureEndpoints(context);
                });
            }
            else
            {
                x.UsingRabbitMq((context, cfg) =>
                {
                    var host = configuration["RabbitMQ:Host"] ?? "localhost";
                    var port = ushort.TryParse(configuration["RabbitMQ:Port"], out var p) ? p : (ushort)5672;
                    var user = configuration["RabbitMQ:Username"] ?? "guest";
                    var pass = configuration["RabbitMQ:Password"] ?? "guest";

                    cfg.Host(host, port, "/", h =>
                    {
                        h.Username(user);
                        h.Password(pass);
                    });

                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        // Services
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();
        services.AddScoped<IEmailService, MockEmailService>();

        // HttpClient for VietQR lookup proxy (BE-CORE-3.1.5)
        services.AddHttpClient("VietQrLookup", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        return services;
    }
}
