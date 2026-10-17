using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Data;
using MockOrganizer.API.Resale;
using MockOrganizer.API.AntiBotScoring;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
var resaleEnabled = builder.Services.AddOrganizerResale(builder.Configuration);
if (resaleEnabled && builder.Environment.IsDevelopment())
{
    var httpPort = builder.Configuration.GetValue("HttpPort", 5001);
    var grpcPort = builder.Configuration.GetValue("OrganizerResale:DevelopmentGrpcPort", 5004);
    builder.WebHost.ConfigureKestrel(k =>
    {
        k.Listen(System.Net.IPAddress.Loopback, httpPort, endpoint => endpoint.Protocols = HttpProtocols.Http1);
        k.Listen(System.Net.IPAddress.Loopback, grpcPort, endpoint => endpoint.Protocols = HttpProtocols.Http2);
    });
}

// Add services to the container.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// US-8.3 (SCRUM-257): Cấu hình và nạp Singleton Anti-Bot ONNX Runtime
builder.Services.Configure<AntiBotScoringOptions>(
    builder.Configuration.GetSection(AntiBotScoringOptions.SectionName));
builder.Services.AddSingleton<IAntiBotScoringService, AntiBotScoringService>();

builder.Services.AddSingleton<MockOrganizer.API.Services.ISessionTokenService, MockOrganizer.API.Services.SessionTokenService>();

// BE-PIPE-8.2.2 (SCRUM-252): Redis Sliding Window Counter cho burst_rate_10s và device_account_overlap
var redisConnectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
var redisEnabled = builder.Configuration.GetValue<bool>("Redis:Enabled", true);

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    if (!redisEnabled) return null!;
    try
    {
        var configuration = ConfigurationOptions.Parse(redisConnectionString);
        configuration.AbortOnConnectFail = false;
        configuration.ConnectTimeout = 1000;
        configuration.SyncTimeout = 1000;
        return ConnectionMultiplexer.Connect(configuration);
    }
    catch (Exception ex)
    {
        var logger = sp.GetService<ILogger<Program>>();
        logger?.LogWarning(ex, "Failed to connect to Redis at {Connection}. Will fallback to In-Memory Sliding Window.", redisConnectionString);
        return null!;
    }
});
builder.Services.AddSingleton<MockOrganizer.API.Services.IRedisSlidingWindowService, MockOrganizer.API.Services.RedisSlidingWindowService>();

builder.Services.AddScoped<MockOrganizer.API.Services.IServerTransactionTelemetryService, MockOrganizer.API.Services.ServerTransactionTelemetryService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=organizer_db;Username=postgres;Password=12345";

builder.Services.AddDbContext<OrganizerDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();
if (resaleEnabled)
{
    using var resaleScope = app.Services.CreateScope();
    try
    {
        await resaleScope.ServiceProvider.GetRequiredService<ResaleStore>().Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "ResaleStore migration skipped or already applied.");
    }
    app.MapGrpcService<OrganizerResaleGrpcService>();
}

// Auto-migrate and seed database on startup (Zero-CLI needed for teammates)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var dbContext = services.GetRequiredService<OrganizerDbContext>();
        await OrganizerDatabaseSeeder.SeedOrganizerAsync(dbContext);
        logger.LogInformation("Organizer database auto-migrated and verified successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while auto-migrating Organizer database.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => Results.Content(MockOrganizer.API.Portal.PortalHtml.Html, "text/html"));
app.MapGet("/portal", () => Results.Content(MockOrganizer.API.Portal.PortalHtml.Html, "text/html"));

app.Run();
