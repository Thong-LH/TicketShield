using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using TicketShield.AntiBot.Gateway.Middleware;
using TicketShield.AntiBot.Gateway.Models;
using TicketShield.AntiBot.Gateway.Services;
using TicketShield.AntiBot.Gateway.Services.Network;

// CLI Self-Test Runner cho NET-13.1 (Verification & Latency Benchmark)
if (args.Contains("--self-test"))
{
    var testParser = new Ja4Parser();
    var testExtractor = new TlsProfileExtractor(testParser, Microsoft.Extensions.Logging.Abstractions.NullLogger<TlsProfileExtractor>.Instance);
    var pseudoInspector = new PseudoHeaderInspector(Microsoft.Extensions.Logging.Abstractions.NullLogger<PseudoHeaderInspector>.Instance);
    var settingsInspector = new Http2SettingsInspector(Microsoft.Extensions.Logging.Abstractions.NullLogger<Http2SettingsInspector>.Instance);
    var testConsistency = new Http2ConsistencyService(pseudoInspector, settingsInspector, Microsoft.Extensions.Logging.Abstractions.NullLogger<Http2ConsistencyService>.Instance);
    var testClientHints = new ClientHintsValidator();
    var testSubnetTracker = new SubnetCorrelationTracker(null, null, null, Microsoft.Extensions.Logging.Abstractions.NullLogger<SubnetCorrelationTracker>.Instance);
    var testNetworkService = new NetworkConsistencyService(testExtractor, testConsistency, testClientHints, testSubnetTracker, Microsoft.Extensions.Logging.Abstractions.NullLogger<NetworkConsistencyService>.Instance);
    var testNonceService = new NonceValidationService();
    var testTokenService = new SessionTokenService(new ConfigurationBuilder().Build());
    var testUaValidator = new UserAgentCrossValidator();

    var report = AntiBotSelfTestRunner.RunAllTests(testParser, testExtractor, testConsistency, testClientHints, testSubnetTracker, testNetworkService, testNonceService, testTokenService, testUaValidator);

    Console.WriteLine("==========================================================");
    Console.WriteLine(" NET-13.1 & US-13.2: PROTOCOL CONSISTENCY & SDK SUITE     ");
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
builder.Services.AddSingleton<INonceValidationService, NonceValidationService>();
builder.Services.AddSingleton<IAntiBotRedisService, AntiBotRedisService>();
builder.Services.AddSingleton<IAntiBotScoringService, AntiBotScoringService>();
builder.Services.AddSingleton<IAntiBotGuardService, AntiBotGuardService>();
builder.Services.AddSingleton<IJa4Parser, Ja4Parser>();
builder.Services.AddSingleton<ITlsProfileExtractor, TlsProfileExtractor>();
builder.Services.AddSingleton<IPseudoHeaderInspector, PseudoHeaderInspector>();
builder.Services.AddSingleton<IHttp2SettingsInspector, Http2SettingsInspector>();
builder.Services.AddSingleton<IHttp2ConsistencyService, Http2ConsistencyService>();
builder.Services.AddSingleton<IClientHintsValidator, ClientHintsValidator>();
builder.Services.AddSingleton<ISubnetCorrelationTracker, SubnetCorrelationTracker>();
builder.Services.AddSingleton<INetworkConsistencyService, NetworkConsistencyService>();
builder.Services.AddSingleton<IUserAgentCrossValidator, UserAgentCrossValidator>();

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

// Endpoint cấp phát Session Token độc lập tại Gateway (SCRUM-272 & US-13.2.3)
app.MapGet("/api/organizer/orders/session", async (
    [FromQuery] Guid tierId,
    [FromServices] ISessionTokenService tokenService,
    [FromServices] INonceValidationService nonceService) =>
{
    if (tierId == Guid.Empty)
        return Results.BadRequest(new { message = "TierId must not be empty." });

    var now = DateTimeOffset.UtcNow;
    var (token, nonce, issuedAt, expiresInSeconds) = tokenService.GenerateSessionTokenWithNonce(tierId, now, 300);

    // Đăng ký Nonce nguyên tử vào Store (Redis + In-Memory Fallback, TTL 300s)
    await nonceService.RegisterNonceAsync(nonce, tierId, TimeSpan.FromSeconds(expiresInSeconds));

    return Results.Ok(new
    {
        sessionToken = token,
        tierId = tierId,
        nonce = nonce,
        issuedAtUtc = issuedAt,
        expiresInSeconds = expiresInSeconds
    });
});

// Endpoint cấp phát Session Token chuẩn REST v1
app.MapPost("/api/v1/antibot/session", async (
    [FromBody] SessionRequestDto request,
    [FromServices] ISessionTokenService tokenService,
    [FromServices] INonceValidationService nonceService) =>
{
    if (request.TierId == Guid.Empty)
        return Results.BadRequest(new { message = "TierId must not be empty." });

    var now = DateTimeOffset.UtcNow;
    var (token, nonce, issuedAt, expiresInSeconds) = tokenService.GenerateSessionTokenWithNonce(request.TierId, now, 300);

    // Đăng ký Nonce nguyên tử vào Store (Redis + In-Memory Fallback, TTL 300s)
    await nonceService.RegisterNonceAsync(nonce, request.TierId, TimeSpan.FromSeconds(expiresInSeconds));

    return Results.Ok(new
    {
        sessionToken = token,
        tierId = request.TierId,
        nonce = nonce,
        issuedAtUtc = issuedAt,
        expiresInSeconds = expiresInSeconds
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

// Endpoint chẩn đoán thông tin HTTP/2 Profile (NET-13.1.2)
app.MapGet("/api/antibot/http2-profile", (
    HttpContext ctx,
    [FromServices] IHttp2ConsistencyService consistencyService) =>
{
    var ua = ctx.Request.Headers.UserAgent.FirstOrDefault();
    var profile = consistencyService.Inspect(ctx, ua);
    return Results.Ok(profile);
});

// Endpoint chẩn đoán thông tin Client Hints (NET-13.1.3)
app.MapGet("/api/antibot/client-hints", (
    HttpContext ctx,
    [FromServices] IClientHintsValidator validator) =>
{
    var profile = validator.Validate(ctx);
    return Results.Ok(profile);
});

// Endpoint chẩn đoán thông tin Subnet Correlation (NET-13.1.3)
app.MapGet("/api/antibot/subnet", async (
    HttpContext ctx,
    [FromServices] ISubnetCorrelationTracker tracker) =>
{
    var clientIp = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                   ?? ctx.Connection.RemoteIpAddress?.ToString();
    var metrics = await tracker.TrackSubnetAsync(clientIp, ctx.RequestAborted);
    return Results.Ok(metrics);
});

// Endpoint chẩn đoán tổng hợp Network Consistency (NET-13.1)
app.MapGet("/api/antibot/network-consistency", async (
    HttpContext ctx,
    [FromServices] INetworkConsistencyService networkService) =>
{
    var summary = await networkService.EvaluateAsync(ctx, ctx.RequestAborted);
    return Results.Ok(summary);
});

// Endpoint tự kiểm thử tự động nội tại (Self-Test Runner NET-13.1 & US-13.2)
app.MapGet("/api/antibot/self-test", (
    [FromServices] IJa4Parser parser,
    [FromServices] ITlsProfileExtractor extractor,
    [FromServices] IHttp2ConsistencyService consistencyService,
    [FromServices] IClientHintsValidator clientHintsValidator,
    [FromServices] ISubnetCorrelationTracker subnetTracker,
    [FromServices] INetworkConsistencyService networkService,
    [FromServices] INonceValidationService nonceService,
    [FromServices] ISessionTokenService tokenService,
    [FromServices] IUserAgentCrossValidator uaValidator) =>
{
    var report = AntiBotSelfTestRunner.RunAllTests(parser, extractor, consistencyService, clientHintsValidator, subnetTracker, networkService, nonceService, tokenService, uaValidator);
    return report.AllPassed ? Results.Ok(report) : Results.Problem(title: "Self-Test Failed", statusCode: 500);
});

// Interceptor Guard Middleware chạy trước YARP Reverse Proxy
app.UseMiddleware<AntiBotCheckoutGuardMiddleware>();

// Reverse Proxy forward toàn bộ các request khác sang MockOrganizer
app.MapReverseProxy();

app.Run();

public record SessionRequestDto(Guid TierId);
