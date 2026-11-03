using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using TicketShield.AntiBot.Gateway.Middleware;
using TicketShield.AntiBot.Gateway.Models;
using TicketShield.AntiBot.Gateway.Services;

var builder = WebApplication.CreateBuilder(args);

var port = builder.Configuration.GetValue("HttpPort", 5006);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(System.Net.IPAddress.Loopback, port);
});

// 1. CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 2. Anti-Bot Scoring & Policy Options
builder.Services.Configure<AntiBotScoringOptions>(
    builder.Configuration.GetSection(AntiBotScoringOptions.SectionName));

// 3. Redis Connection (Multiplexer)
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
    catch
    {
        return null!;
    }
});

// 4. Core Anti-Bot Services
builder.Services.AddSingleton<ISessionTokenService, SessionTokenService>();
builder.Services.AddSingleton<IAntiBotRedisService, AntiBotRedisService>();
builder.Services.AddSingleton<IAntiBotScoringService, AntiBotScoringService>();
builder.Services.AddSingleton<IAntiBotGuardService, AntiBotGuardService>();

// 5. YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseCors("AllowAll");

// Health check endpoint
app.MapGet("/health", (
    [FromServices] IAntiBotScoringService scoring,
    [FromServices] IConnectionMultiplexer? redis) =>
{
    var redisConnected = redis != null && redis.IsConnected;
    return Results.Ok(new
    {
        service = "TicketShield.AntiBot.Gateway",
        status = "Healthy",
        targetUpstream = "http://localhost:5001",
        port = port,
        redisAvailable = redisConnected,
        timestamp = DateTime.UtcNow
    });
});

// Endpoint cấp phát Session Token độc lập tại Gateway (SCRUM-272)
app.MapGet("/api/organizer/orders/session", (
    [FromQuery] Guid tierId,
    [FromServices] ISessionTokenService tokenService) =>
{
    if (tierId == Guid.Empty)
        return Results.BadRequest(new { message = "TierId must not be empty." });

    var now = DateTimeOffset.UtcNow;
    var token = tokenService.GenerateSessionToken(tierId, now);

    return Results.Ok(new
    {
        sessionToken = token,
        tierId = tierId,
        issuedAtUtc = now,
        expiresInSeconds = 7200
    });
});

// Endpoint cấp phát Session Token chuẩn REST v1
app.MapPost("/api/v1/antibot/session", (
    [FromBody] SessionRequestDto request,
    [FromServices] ISessionTokenService tokenService) =>
{
    if (request.TierId == Guid.Empty)
        return Results.BadRequest(new { message = "TierId must not be empty." });

    var now = DateTimeOffset.UtcNow;
    var token = tokenService.GenerateSessionToken(request.TierId, now);

    return Results.Ok(new
    {
        sessionToken = token,
        tierId = request.TierId,
        issuedAtUtc = now,
        expiresInSeconds = 7200
    });
});

// Interceptor Guard Middleware chạy trước YARP Reverse Proxy
app.UseMiddleware<AntiBotCheckoutGuardMiddleware>();

// Reverse Proxy forward toàn bộ các request khác sang MockOrganizer
app.MapReverseProxy();

app.Run();

public record SessionRequestDto(Guid TierId);
