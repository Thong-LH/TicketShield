using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketShield.Infrastructure.Messaging.Consumers;

namespace TicketShield.Infrastructure.Messaging;

public static class EventBusRegistration
{
    public static IServiceCollection AddEventBus(this IServiceCollection services, IConfiguration configuration)
    {
        var useInMemory = configuration.GetValue("RabbitMQ:UseInMemory", false);
        var host = configuration["RabbitMQ:Host"] ?? "localhost";
        var port = configuration.GetValue("RabbitMQ:Port", (ushort)5672);
        var username = configuration["RabbitMQ:Username"] ?? "guest";
        var password = configuration["RabbitMQ:Password"] ?? "guest";
        var virtualHost = configuration["RabbitMQ:VirtualHost"] ?? "/";

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            // Đăng ký Consumers đồng bộ Shadow User và Hold Expiry
            x.AddConsumer<UserCreatedConsumer>();
            x.AddConsumer<UserProfileUpdatedConsumer>();
            x.AddConsumer<HoldExpiredConsumer>();

            x.AddDelayedMessageScheduler();

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
                    cfg.Host(host, port, virtualHost, h =>
                    {
                        h.Username(username);
                        h.Password(password);
                    });

                    cfg.UseDelayedMessageScheduler();
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        services.AddScoped<TicketShield.Application.Common.Interfaces.IMessageSchedulerService, Services.MassTransitMessageSchedulerService>();

        return services;
    }
}
