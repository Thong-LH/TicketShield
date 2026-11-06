using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        services.AddDisputeEvidenceStore(configuration);
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

    // Đủ 3 khoá Cloudinary → Cloudinary; trống cả 3 → ổ đĩa; thiếu một phần → dừng khởi động (SCRUM-268).
    public static IServiceCollection AddDisputeEvidenceStore(this IServiceCollection services, IConfiguration configuration)
    {
        var cloudinary = configuration.GetSection(Services.CloudinaryOptions.SectionName).Get<Services.CloudinaryOptions>()
            ?? new Services.CloudinaryOptions();
        var keys = new (string Name, string Value)[]
        {
            ("Cloudinary:CloudName", cloudinary.CloudName),
            ("Cloudinary:ApiKey", cloudinary.ApiKey),
            ("Cloudinary:ApiSecret", cloudinary.ApiSecret)
        };
        var missing = keys.Where(key => string.IsNullOrWhiteSpace(key.Value)).Select(key => key.Name).ToList();

        if (missing.Count == 0)
        {
            services.AddHttpClient(Services.CloudinaryEvidenceFileStore.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            services.AddSingleton<IDisputeEvidenceFileStore>(provider =>
            {
                var logger = provider.GetService<ILogger<Services.CloudinaryEvidenceFileStore>>()
                    ?? NullLogger<Services.CloudinaryEvidenceFileStore>.Instance;
                logger.LogInformation("Ảnh bằng chứng khiếu nại lưu trên Cloudinary, cloud {CloudName}.", cloudinary.CloudName);
                return new Services.CloudinaryEvidenceFileStore(cloudinary, provider.GetRequiredService<IHttpClientFactory>(), logger);
            });
            return services;
        }

        if (missing.Count < keys.Length)
        {
            throw new InvalidOperationException("Thiếu cấu hình Cloudinary: " + string.Join(", ", missing));
        }

        services.AddSingleton<IDisputeEvidenceFileStore>(provider =>
        {
            var path = configuration["DisputeEvidence:StoragePath"];
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, "dispute-evidence");
            }

            provider.GetService<ILogger<Services.DisputeEvidenceFileStore>>()
                ?.LogInformation("Ảnh bằng chứng khiếu nại lưu xuống ổ đĩa tại {Path}.", path);
            return new Services.DisputeEvidenceFileStore(path);
        });

        return services;
    }
}
