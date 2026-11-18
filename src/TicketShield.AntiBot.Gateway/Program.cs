using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using TicketShield.AntiBot.Gateway.Middleware;
using TicketShield.AntiBot.Gateway.Models;
using TicketShield.AntiBot.Gateway.Services;
using TicketShield.AntiBot.Gateway.Services.Network;

// CLI Self-Test Runner cho NET-13.1.1 (Verification & Latency Benchmark)
if (args.Contains("--self-test"))
{
    var testParser = new Ja4Parser();
    var testExtractor = new TlsProfileExtractor(testParser, Microsoft.Extensions.Logging.Abstractions.NullLogger<TlsProfileExtractor>.Instance);
    var report = AntiBotSelfTestRunner.RunAllTests(testParser, testExtractor);

    Console.WriteLine("==========================================================");
    Console.WriteLine("      NET-13.1.1: SELF-TEST & BENCHMARK VERIFICATION       ");
    Console.WriteLine("==========================================================");
    foreach (var r in report.Results)
    {
        Console.ForegroundColor = r.Passed ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"[{(r.Passed ? "PASS" : "FAIL")}] {r.TestName} ({r.LatencyMicroseconds:F2} us)");
        Console.WriteLine($"       Details: {r.Details}");
        Console.ResetColor();
    }
    Console.WriteLine();
    Console.WriteLine($"Summary: {report.PassedTests}/{report.TotalTests} tests passed.");
    Console.WriteLine($"Average Benchmark Latency: {report.AverageLatencyMicroseconds:F4} us (SLA Target: < 50 us)");
    Console.WriteLine(report.AllPassed ? "STATUS: SUCCESS (100% PASSED)" : "STATUS: FAILED");

    Environment.Exit(report.AllPassed ? 0 : 1);
}

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
builder.Services.AddSingleton<IJa4Parser, Ja4Parser>();
builder.Services.AddSingleton<ITlsProfileExtractor, TlsProfileExtractor>();

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

// Endpoint chẩn đoán thông tin TLS Profile (NET-13.1.1)
app.MapGet("/api/antibot/tls-profile", (
    HttpContext ctx,
    [FromServices] ITlsProfileExtractor extractor) =>
{
    var profile = extractor.Extract(ctx);
    return Results.Ok(profile);
});

// Endpoint tự kiểm thử tự động nội tại (Self-Test Runner NET-13.1.1)
app.MapGet("/api/antibot/self-test", (
    [FromServices] IJa4Parser parser,
    [FromServices] ITlsProfileExtractor extractor) =>
{
    var report = AntiBotSelfTestRunner.RunAllTests(parser, extractor);
    return report.AllPassed ? Results.Ok(report) : Results.Problem(title: "Self-Test Failed", statusCode: 500);
});

// Interceptor Guard Middleware chạy trước YARP Reverse Proxy
app.UseMiddleware<AntiBotCheckoutGuardMiddleware>();

// Reverse Proxy forward toàn bộ các request khác sang MockOrganizer
app.MapReverseProxy();

app.Run();

public record SessionRequestDto(Guid TierId);
