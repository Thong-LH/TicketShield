using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MockOrganizer.API.Data;
using MockOrganizer.API.Resale;
using Npgsql;
using TicketShield.API.Controllers;
using TicketShield.API.Resale;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Persistence.Resale;
using TicketShield.Infrastructure.ExternalServices.Organizer;
using TicketShield.Infrastructure.Resale;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace TicketShield.Resale.Tests;

public sealed class MutableClock : TimeProvider
{
    private long ticks = DateTimeOffset.UtcNow.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
}

public sealed class ResaleFixture : IAsyncLifetime
{
    public const string Seller = "11111111-1111-1111-1111-111111111111";
    public const string OtherSeller = "22222222-2222-2222-2222-222222222222";
    public const string Organizer = "e0000000-0000-0000-0000-000000000001";
    public const string Event = "e1111111-1111-1111-1111-111111111111";
    public const string Tier = "d1111111-1111-1111-1111-111111111111";
    public PostgresCluster Database { get; } = new();
    public SmtpCapture Mail { get; } = new();
    public MutableClock Clock { get; } = new();
    public WebApplication Mock { get; private set; } = null!;
    public WebApplication Core { get; private set; } = null!;
    public HttpClient Http { get; private set; } = null!;
    public GrpcChannel Channel { get; private set; } = null!;
    public OrganizerResaleService.OrganizerResaleServiceClient Grpc { get; private set; } = null!;
    private string apiKey = "";
    private string signingKey = "";
    public Metadata Headers => new() { { "authorization", "Bearer " + apiKey } };
    public static string Id() => Guid.NewGuid().ToString("D");
    public async Task InitializeAsync()
    {
        await Database.Start(); Mail.Start();
        apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        signingKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var grpcPort = PostgresCluster.FreePort(); var httpPort = PostgresCluster.FreePort();
        var mockBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        mockBuilder.Logging.ClearProviders();
        mockBuilder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, grpcPort, l => l.Protocols = HttpProtocols.Http2));
        mockBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = Database.MockConnection,
            ["OrganizerResale:Enabled"] = "true", ["OrganizerResale:OrganizerId"] = Organizer,
            ["OrganizerResale:ApiKey"] = apiKey, ["OrganizerResale:HmacKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ["OrganizerResale:Smtp:Host"] = "127.0.0.1", ["OrganizerResale:Smtp:Port"] = Mail.Port.ToString(),
            ["OrganizerResale:Smtp:EnableSsl"] = "false", ["OrganizerResale:Smtp:Sender"] = "test@ticketshield.invalid",
            ["OrganizerResale:RequestsPerRequesterPerHour"] = "200", ["OrganizerResale:FailuresPerRequesterPerHour"] = "200"
        });
        mockBuilder.Services.AddSingleton<TimeProvider>(Clock);
        mockBuilder.Services.AddDbContext<OrganizerDbContext>(o => o.UseNpgsql(Database.MockConnection));
        mockBuilder.Services.AddOrganizerResale(mockBuilder.Configuration);
        Mock = mockBuilder.Build();
        using (var scope = Mock.Services.CreateScope()) {
            await OrganizerDatabaseSeeder.SeedOrganizerAsync(scope.ServiceProvider.GetRequiredService<OrganizerDbContext>());
            await scope.ServiceProvider.GetRequiredService<ResaleStore>().Database.MigrateAsync();
        }
        Mock.MapGrpcService<OrganizerResaleGrpcService>(); await Mock.StartAsync();
        Channel = GrpcChannel.ForAddress($"http://127.0.0.1:{grpcPort}"); Grpc = new(Channel);

        var coreBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        coreBuilder.Logging.ClearProviders();
        coreBuilder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, httpPort, l => l.Protocols = HttpProtocols.Http1));
        coreBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = Database.CoreConnection,
            ["OrganizerGrpc:Enabled"] = "true", ["OrganizerGrpc:OrganizerId"] = Organizer,
            ["OrganizerGrpc:Address"] = $"http://127.0.0.1:{grpcPort}", ["OrganizerGrpc:ApiKey"] = apiKey,
            ["OrganizerGrpc:HmacKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ["OrganizerGrpc:Mappings:vip:ExternalEventId"] = "concert", ["OrganizerGrpc:Mappings:vip:ExternalTierId"] = "vip",
            ["OrganizerGrpc:Mappings:vip:EventId"] = Event, ["OrganizerGrpc:Mappings:vip:TierId"] = Tier,
            ["ResaleJwt:SigningKey"] = signingKey, ["ResaleJwt:Issuer"] = "test-issuer", ["ResaleJwt:Audience"] = "test-core"
        });
        coreBuilder.Services.AddSingleton<TimeProvider>(Clock);
        coreBuilder.Services.AddDbContext<TicketShieldDbContext>(o => o.UseNpgsql(Database.CoreConnection));
        coreBuilder.Services.AddCoreResale(coreBuilder.Configuration, coreBuilder.Environment);
        coreBuilder.Services.AddResaleAuthentication(coreBuilder.Configuration);
        coreBuilder.Services.AddControllers().AddApplicationPart(typeof(ResaleController).Assembly);
        Core = coreBuilder.Build();
        using (var scope = Core.Services.CreateScope()) {
            await DatabaseSeeder.SeedTicketShieldAsync(scope.ServiceProvider.GetRequiredService<TicketShieldDbContext>());
            await scope.ServiceProvider.GetRequiredService<CoreResaleStore>().Database.MigrateAsync();
        }
        Core.UseAuthentication(); Core.UseAuthorization(); Core.MapControllers(); await Core.StartAsync();
        Http = new() { BaseAddress = new Uri($"http://127.0.0.1:{httpPort}") };
    }
    public async Task<string> Ticket(string status = "VALID", decimal price = 2500000m)
    {
        var code = "TEST-" + Guid.NewGuid().ToString("N");
        await Sql(Database.MockConnection, "INSERT INTO mock_tickets (id,ticket_code,event_name,seat_zone,original_price,owner_email,status,created_at,updated_at) VALUES (@id,@code,'Test concert','VIP',@price,'seller@example.invalid',@status,now(),now())",
            ("id", Guid.NewGuid()), ("code", code), ("price", price), ("status", status));
        Mock.Services.GetRequiredService<ResaleOptions>().TicketMappings[code] = new() { EventId = "concert", TierId = "vip" };
        return code;
    }
    public async Task<object?> Sql(string connectionString, string query, params (string Key, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(query, connection);
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
        return await command.ExecuteScalarAsync();
    }
    public string Token(string seller) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("test-issuer", "test-core",
        [new Claim("sub", seller)], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1),
        new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256)));
    public async Task<(HttpStatusCode Status, JsonElement Body)> Post(string path, object body, string? key = null, string? seller = Seller)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Id());
        if (seller is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(seller));
        using var response = await Http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(string.IsNullOrEmpty(content) ? "{}" : content);
        return (response.StatusCode, json.RootElement.Clone());
    }
    public async Task<(HttpStatusCode Status, JsonElement Body)> Get(string path, string seller = Seller)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(seller));
        using var response = await Http.SendAsync(request);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, json.RootElement.Clone());
    }
    public async Task<(string Verification, string Otp)> Start(string? ticket = null)
    {
        ticket ??= await Ticket();
        var result = await Post("api/ticket-verifications", new { ticketCode = ticket });
        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal("SmtpAccepted", result.Body.GetProperty("data").GetProperty("deliveryState").GetString());
        return (result.Body.GetProperty("data").GetProperty("verificationId").GetString()!, Mail.Messages.Last().Otp);
    }
    public async Task DisposeAsync()
    {
        Http?.Dispose(); Channel?.Dispose();
        if (Core is not null) await Core.DisposeAsync();
        if (Mock is not null) await Mock.DisposeAsync();
        await Mail.DisposeAsync(); await Database.DisposeAsync();
    }
}
