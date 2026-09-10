using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MockOrganizer.API.Resale;

public static class ResaleRegistration
{
    public static bool AddOrganizerResale(this IServiceCollection services, IConfiguration configuration)
    {
        var o = configuration.GetSection("OrganizerResale").Get<ResaleOptions>() ?? new();
        if (!o.Enabled) return false;
        if (o.ApiKey.Length < 32 || o.HmacKey.Length < 32 || !Guid.TryParseExact(o.OrganizerId, "D", out _) ||
            o.DevelopmentGrpcPort is < 1 or > 65535 || o.OtpLifetimeSeconds <= 0 || o.ResendCooldownSeconds < 1 || o.MaxAttempts < 1 || o.MaxResends < 0 ||
            o.RequestsPerTicketPerHour < 1 || o.RequestsPerRequesterPerHour < 1 || o.FailuresPerTicketPerHour < 1 || o.FailuresPerRequesterPerHour < 1)
            throw new InvalidOperationException("OrganizerResale requires valid identity, secret keys (32+ characters) and positive policy limits.");
        services.AddSingleton(o);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IOtpDelivery, SmtpOtpDelivery>();
        services.AddDbContext<ResaleStore>(b => b.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
            pg => pg.MigrationsHistoryTable("__OrganizerResaleMigrationsHistory")));
        services.AddGrpc(g => { g.EnableDetailedErrors = false; g.MaxReceiveMessageSize = 16 * 1024; g.MaxSendMessageSize = 64 * 1024; });
        return true;
    }
}
