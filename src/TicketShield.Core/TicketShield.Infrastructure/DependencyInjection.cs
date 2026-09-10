using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        return services;
    }
}
