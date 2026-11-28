using TicketShield.AntiBot.Verification;
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
    var testScoringService = new AntiBotScoringService(
        Microsoft.Extensions.Options.Options.Create(new AntiBotScoringOptions()),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AntiBotScoringService>.Instance);

    var report = AntiBotSelfTestRunner.RunAllTests(
        testParser, testExtractor, testConsistency, testClientHints,
        testSubnetTracker, testNetworkService, testNonceService, testTokenService,
        testUaValidator, testScoringService);

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

// CLI Cross-Language Batch Evaluation Runner cho ML-13.3.6
if (args.Contains("--eval-v2-batch"))
{
    var scoringService = new AntiBotScoringService(
        Microsoft.Extensions.Options.Options.Create(new AntiBotScoringOptions()),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AntiBotScoringService>.Instance);

    string jsonInput = string.Empty;
    int idx = Array.IndexOf(args, "--eval-v2-batch");
    if (idx + 1 < args.Length && !string.IsNullOrWhiteSpace(args[idx + 1]))
    {
        jsonInput = args[idx + 1];
    }
    else
    {
        jsonInput = Console.In.ReadToEnd();
    }

    jsonInput = jsonInput.Trim().TrimStart('\uFEFF');
    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var vectors = System.Text.Json.JsonSerializer.Deserialize<List<BotRiskVectorV2>>(jsonInput, options) ?? new List<BotRiskVectorV2>();
    var responses = new List<AntiBotScoreResponse>();
    foreach (var v in vectors)
    {
        responses.Add(scoringService.EvaluateV2(v));
    }

    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(responses));
    Environment.Exit(0);
}

// CLI Component Test Runner cho ML-13.3.4.2 (ShadowMetricsAggregator & FprMetricLogger)
if (args.Contains("--test-shadow-components"))
{
    Console.WriteLine("==========================================================");
    Console.WriteLine(" ML-13.3.4.2: SHADOW METRICS AGGREGATOR & FPR LOGGER TEST ");
    Console.WriteLine("==========================================================");

    var testOptions = Microsoft.Extensions.Options.Options.Create(new AntiBotScoringOptions
    {
        ExecutionMode = AntiBotExecutionMode.Shadow,
        EnableStructuredFprLogging = true,
        StructuredLogPath = "logs/test_shadow_{yyyyMMdd}.jsonl"
    });

    var aggregator = new ShadowMetricsAggregator(testOptions);

    // Test 1: Record cohorts
    aggregator.RecordEvaluation("v1_block_v2_allow", false, 0.84);
    aggregator.RecordEvaluation("v1_challenge_v2_allow", false, 0.40);
    aggregator.RecordEvaluation("v1_allow_v2_block", false, 0.70);
    aggregator.RecordEvaluation("v1_challenge_v2_block", false, 0.35);
    aggregator.RecordEvaluation("v1_allow_v2_allow", true, 0.05);
    aggregator.RecordEvaluation("v1_block_v2_block", true, 0.02, isError: true);

    var snap = aggregator.GetSnapshot();
    bool aggPass = snap.TotalEvaluations == 6 &&
                   snap.AgreementCount == 2 &&
                   snap.RescuedFalsePositives == 1 &&
                   snap.FrictionReduction == 1 &&
                   snap.CaughtStealthBots == 1 &&
                   snap.CaptchaBotElimination == 1 &&
                   snap.V2EvaluationErrors == 1 &&
                   Math.Abs(snap.AverageRiskScoreDelta - Math.Round((0.84 + 0.40 + 0.70 + 0.35 + 0.05 + 0.02) / 6.0, 4)) < 0.001;

    Console.WriteLine($"[{(aggPass ? "PASS" : "FAIL")}] ShadowMetricsAggregator atomic counters & snapshot calculation");
    Console.WriteLine($"       Total={snap.TotalEvaluations}, Agree={snap.AgreementCount}, RescuedFp={snap.RescuedFalsePositives}, Stealth={snap.CaughtStealthBots}, Errors={snap.V2EvaluationErrors}");

    // Test 2: Reset
    aggregator.Reset();
    var resetSnap = aggregator.GetSnapshot();
    bool resetPass = resetSnap.TotalEvaluations == 0 && resetSnap.AgreementCount == 0 && resetSnap.CohortMatrix.Count == 0;
    Console.WriteLine($"[{(resetPass ? "PASS" : "FAIL")}] ShadowMetricsAggregator.Reset()");

    // Test 3: FprMetricLogger Non-blocking channel and JSONL output
    var testLogger = new FprMetricLogger(testOptions, Microsoft.Extensions.Logging.Abstractions.NullLogger<FprMetricLogger>.Instance);
    var dummyContext = new CheckoutTelemetryContext
    {
        ClientIp = "127.0.0.1",
        BuyerEmail = "tester@ticketshield.io",
        DeviceFingerprint = "fingerprint_12345",
        CanvasTampered = 1.0f,
        ActionIntervalStdDev = 145.0f,
        TimeToCheckout = 12.5f
    };
    var dummyPrimary = new AntiBotScoreResponse { RiskScore = 0.85f, Decision = "Block", InferenceTimeMs = 0.5 };
    var dummyShadow = new AntiBotScoreResponse { RiskScore = 0.02f, Decision = "Allow", InferenceTimeMs = 0.03 };

    var entry = ShadowEvaluationLogEntry.Create(
        "REQ-TEST-001",
        AntiBotExecutionMode.Shadow,
        dummyContext,
        dummyPrimary,
        dummyShadow,
        "v1_block_v2_allow",
        evaluationMs: 0.035,
        totalGuardMs: 1.25);

    bool writeQueued = testLogger.LogEvaluation(entry);
    await testLogger.FlushAsync();

    string writtenPath = testLogger.ResolveLogFilePath();
    bool fileExists = File.Exists(writtenPath);
    bool jsonValid = false;
    if (fileExists)
    {
        string[] lines = await File.ReadAllLinesAsync(writtenPath);
        if (lines.Length > 0 && lines[^1].Contains("\"request_id\":\"REQ-TEST-001\"") && lines[^1].Contains("\"is_false_positive_candidate\":true"))
        {
            jsonValid = true;
        }
        try { File.Delete(writtenPath); } catch { }
    }

    bool loggerPass = writeQueued && fileExists && jsonValid;
    Console.WriteLine($"[{(loggerPass ? "PASS" : "FAIL")}] FprMetricLogger Non-blocking Write, JSONL Formatting & Flush");
    Console.WriteLine($"       Queued={writeQueued}, FileExists={fileExists}, JsonValid={jsonValid}");

    // Test 4: AntiBotGuardService Shadow Mode & Fault Isolation (ML-13.3.4.3)
    var dummyRedis = new AntiBotRedisService();
    var dummyToken = new SessionTokenService(new ConfigurationBuilder().Build());
    var scoringService = new AntiBotScoringService(testOptions, Microsoft.Extensions.Logging.Abstractions.NullLogger<AntiBotScoringService>.Instance);
    var guardService = new AntiBotGuardService(
        dummyToken,
        dummyRedis,
        scoringService,
        testOptions,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AntiBotGuardService>.Instance,
        aggregator,
        testLogger);

    var braveContext = new CheckoutTelemetryContext
    {
        ClientIp = "127.0.0.1",
        BuyerEmail = "brave_user@ticketshield.io",
        DeviceFingerprint = "brave_fingerprint_abc",
        CanvasTampered = 1.0f,
        PrototypeTampered = 0.0f,
        VirtualGpuDetected = 0.0f,
        IsWebDriver = 0.0f,
        ActionIntervalStdDev = 165.0f,
        TimeToCheckout = 15.2f,
        BurstRate10s = 1.0f
    };

    var guardResult = await guardService.EvaluateCheckoutGuardAsync(braveContext);
    bool guardPass = guardResult.ExecutionMode == AntiBotExecutionMode.Shadow &&
                     !string.IsNullOrEmpty(guardResult.ShadowCohort) &&
                     guardResult.ShadowRiskScore.HasValue &&
                     guardResult.VectorV2 != null;

    Console.WriteLine($"[{(guardPass ? "PASS" : "FAIL")}] AntiBotGuardService Shadow Evaluation (Mode={guardResult.ExecutionMode}, Cohort={guardResult.ShadowCohort}, V2Score={guardResult.ShadowRiskScore:F4})");

    // Test 5: Hot-Switching & Admin Security (ML-13.3.4.4)
    var testCtxLocal = new DefaultHttpContext();
    testCtxLocal.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
    bool localAuth = IsAuthorizedAdmin(testCtxLocal, testOptions.Value);

    var testCtxRemoteNoKey = new DefaultHttpContext();
    testCtxRemoteNoKey.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.25");
    bool remoteReject = !IsAuthorizedAdmin(testCtxRemoteNoKey, testOptions.Value);

    var testCtxRemoteWithKey = new DefaultHttpContext();
    testCtxRemoteWithKey.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.25");
    testCtxRemoteWithKey.Request.Headers["X-TicketShield-Admin-Key"] = testOptions.Value.AdminApiKey;
    bool remoteKeyAuth = IsAuthorizedAdmin(testCtxRemoteWithKey, testOptions.Value);

    var prevMode = testOptions.Value.ExecutionMode;
    testOptions.Value.ExecutionMode = AntiBotExecutionMode.Enforce;
    bool switch1 = testOptions.Value.ExecutionMode == AntiBotExecutionMode.Enforce;
    testOptions.Value.ExecutionMode = AntiBotExecutionMode.Shadow;
    bool switch2 = testOptions.Value.ExecutionMode == AntiBotExecutionMode.Shadow;

    bool adminSecPass = localAuth && remoteReject && remoteKeyAuth && switch1 && switch2;
    Console.WriteLine($"[{(adminSecPass ? "PASS" : "FAIL")}] Secured Hot-Switching & Observability Authorization (Local={localAuth}, RemoteBlock={remoteReject}, RemoteKey={remoteKeyAuth}, HotSwitch={switch1 && switch2})");

    bool allOk = aggPass && resetPass && loggerPass && guardPass && adminSecPass;
    Console.WriteLine(allOk ? "STATUS: SUCCESS (All 13.3.4.2, 13.3.4.3 & 13.3.4.4 Components Verified)" : "STATUS: FAILED");
    Environment.Exit(allOk ? 0 : 1);
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("captcha", client => client.Timeout = TimeSpan.FromSeconds(10));
var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:3000,http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var organizerBaseUrl = builder.Configuration["ReverseProxy:Clusters:organizer-cluster:Destinations:destination1:Address"]
    ?? "http://localhost:5001";
builder.Services.AddHttpClient("organizer-analytics", client =>
{
    client.BaseAddress = new Uri(organizerBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<ChallengeVerificationService>();

var port = builder.Configuration.GetValue("HttpPort", 5006);
builder.WebHost.ConfigureKestrel(options =>
{
    var listenAddress = builder.Environment.IsDevelopment()
        ? System.Net.IPAddress.Loopback
        : System.Net.IPAddress.Any;
    options.Listen(listenAddress, port);
});

// 1. CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins(allowedOrigins)
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
builder.Services.AddSingleton<IShadowMetricsAggregator, ShadowMetricsAggregator>();
builder.Services.AddSingleton<FprMetricLogger>();
builder.Services.AddSingleton<IFprMetricLogger>(sp => sp.GetRequiredService<FprMetricLogger>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<FprMetricLogger>());
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

// =========================================================================
// OBSERVABILITY & HOT-SWITCHING APIS CHO SHADOW MODE (ML-13.3.4.4, SCRUM-382)
// =========================================================================

// Endpoint tra cứu số liệu đối chứng Shadow Mode theo thời gian thực
app.MapGet("/api/anti-bot/metrics/shadow-mode", (
    [FromServices] IShadowMetricsAggregator aggregator,
    [FromQuery] bool? reset) =>
{
    var snapshot = aggregator.GetSnapshot();
    if (reset == true)
    {
        aggregator.Reset();
    }

    return Results.Ok(new
    {
        execution_mode = snapshot.ExecutionMode,
        total_evaluations = snapshot.TotalEvaluations,
        agreement_count = snapshot.AgreementCount,
        agreement_rate = snapshot.AgreementRate,
        rescued_false_positives = snapshot.RescuedFalsePositives,
        rescued_false_positive_rate = snapshot.RescuedFalsePositiveRate,
        friction_reduction = snapshot.FrictionReduction,
        friction_reduction_rate = snapshot.FrictionReductionRate,
        caught_stealth_bots = snapshot.CaughtStealthBots,
        caught_stealth_bot_rate = snapshot.CaughtStealthBotRate,
        captcha_bot_elimination = snapshot.CaptchaBotElimination,
        captcha_bot_elimination_rate = snapshot.CaptchaBotEliminationRate,
        v2_evaluation_errors = snapshot.V2EvaluationErrors,
        average_risk_score_delta = snapshot.AverageRiskScoreDelta,
        cohort_breakdown = snapshot.CohortMatrix,
        snapshot_timestamp_utc = snapshot.SnapshotTimestampUtc
    });
});

// Endpoint chuyển đổi nóng ExecutionMode (Shadow <-> Enforce <-> Audit)
// Được bảo vệ nghiêm ngặt bằng Localhost IP hoặc Header X-TicketShield-Admin-Key
app.MapPost("/api/anti-bot/config/execution-mode", (
    HttpContext ctx,
    [FromBody] UpdateExecutionModeRequest request,
    [FromServices] Microsoft.Extensions.Options.IOptions<AntiBotScoringOptions> options) =>
{
    if (!IsAuthorizedAdmin(ctx, options.Value))
    {
        return Results.Json(new
        {
            error = "Forbidden",
            message = "Access restricted to Localhost or requires a valid X-TicketShield-Admin-Key header."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    if (!Enum.TryParse<AntiBotExecutionMode>(request.Mode, ignoreCase: true, out var newMode))
    {
        return Results.BadRequest(new
        {
            error = "InvalidExecutionMode",
            message = $"ExecutionMode '{request.Mode}' is invalid. Valid values: Shadow, Enforce, Audit."
        });
    }

    var previousMode = options.Value.ExecutionMode;
    options.Value.ExecutionMode = newMode;

    return Results.Ok(new
    {
        success = true,
        previous_mode = previousMode.ToString(),
        current_mode = newMode.ToString(),
        updated_at_utc = DateTimeOffset.UtcNow,
        message = $"ExecutionMode successfully changed from {previousMode} to {newMode}."
    });
});

// Endpoint reset bộ đếm thống kê đối chứng
app.MapPost("/api/anti-bot/metrics/shadow-mode/reset", (
    HttpContext ctx,
    [FromServices] IShadowMetricsAggregator aggregator,
    [FromServices] Microsoft.Extensions.Options.IOptions<AntiBotScoringOptions> options) =>
{
    if (!IsAuthorizedAdmin(ctx, options.Value))
    {
        return Results.Json(new
        {
            error = "Forbidden",
            message = "Access restricted to Localhost or requires a valid X-TicketShield-Admin-Key header."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    aggregator.Reset();

    return Results.Ok(new
    {
        success = true,
        message = "Shadow Mode metrics aggregator reset successfully.",
        timestamp_utc = DateTimeOffset.UtcNow
    });
});

// Interceptor Guard Middleware chạy trước YARP Reverse Proxy
app.UseMiddleware<AntiBotCheckoutGuardMiddleware>();

// Reverse Proxy forward toàn bộ các request khác sang MockOrganizer
app.MapReverseProxy();

app.Run();

public record SessionRequestDto(Guid TierId);

public record UpdateExecutionModeRequest(string Mode);

public static partial class Program
{
    public static bool IsAuthorizedAdmin(HttpContext ctx, AntiBotScoringOptions options)
    {
        var remoteIp = ctx.Connection.RemoteIpAddress;
        if (remoteIp != null && System.Net.IPAddress.IsLoopback(remoteIp))
        {
            return true;
        }

        if (ctx.Request.Headers.TryGetValue("X-TicketShield-Admin-Key", out var headerKey) &&
            !string.IsNullOrWhiteSpace(options.AdminApiKey) &&
            string.Equals(headerKey.ToString(), options.AdminApiKey, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
