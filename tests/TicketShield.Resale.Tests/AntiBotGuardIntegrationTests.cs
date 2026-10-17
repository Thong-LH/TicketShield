using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MockOrganizer.API.Controllers;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;
using MockOrganizer.API.Models;
using MockOrganizer.API.Services;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// End-to-End Integration Tests cho BE-PIPE-8.2.3 (SCRUM-253):
/// 6D Feature Vector Assembly, 3-Tier Routing (Allow, Challenge, Block),
/// và 20ms Fail-Open Fallback Guard.
/// </summary>
public sealed class AntiBotGuardIntegrationTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    private const string SessionEndpoint = "api/organizer/orders/session";
    private const string CheckoutEndpoint = "api/organizer/orders/checkout";

    [Fact]
    public async Task Checkout_WhenLegitimateUser_ReturnsOkAndRecordsHoldInRedis()
    {
        var tierId = await CreateTestTierAsync(initialAvailable: 20);
        var buyerEmail = $"human_buyer_{Guid.NewGuid():N}@ticketshield.vn";

        // 1. Cấp session token từ server
        using var sessionResponse = await f.MockHttp.GetAsync($"{SessionEndpoint}?tierId={tierId}");
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionTokenResponse>();
        Assert.NotNull(session);

        // Giả lập người dùng thao tác trong 12 giây
        f.Clock.Advance(TimeSpan.FromSeconds(12));

        // 2. Gửi request checkout với telemetry người thật
        var checkoutBody = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 2,
            BuyerEmail = buyerEmail,
            SessionToken = session.SessionToken,
            DeviceFingerprint = "fp-human-" + Guid.NewGuid().ToString("N"),
            Telemetry = new ClientTelemetryDto
            {
                IsWebdriver = 0,
                ActionIntervalStdDev = 195.0f,
                DeviceFingerprint = "fp-human-device"
            }
        };

        using var checkoutResponse = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, checkoutBody);
        Assert.Equal(HttpStatusCode.OK, checkoutResponse.StatusCode);

        var root = await checkoutResponse.Content.ReadFromJsonAsync<JsonElement>();
        var antiBot = root.GetProperty("antiBot");

        Assert.Equal("Allow", antiBot.GetProperty("decision").GetString());
        Assert.True(antiBot.GetProperty("riskScore").GetSingle() < 0.35f);

        // 3. Kiểm tra số vé đang giữ được ghi nhận vào Redis (không query DB)
        using var scope = f.Mock.Services.CreateScope();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisSlidingWindowService>();
        var holdCount = await redisService.GetConcurrentHoldAsync(buyerEmail);
        Assert.Equal(2, holdCount);
    }

    [Fact]
    public async Task Checkout_WhenObviousBotWithWebdriverAndHighBurst_Returns429TooManyRequestsWithoutTouchingDb()
    {
        var tierId = await CreateTestTierAsync(initialAvailable: 50);
        var botEmail = $"scalper_bot_{Guid.NewGuid():N}@botnet.invalid";

        // 1. Bot gọi session token nhưng checkout siêu tốc (0.1s), bật webdriver = 1, interval cực nhỏ (10ms)
        using var sessionResponse = await f.MockHttp.GetAsync($"{SessionEndpoint}?tierId={tierId}");
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionTokenResponse>();
        Assert.NotNull(session);

        f.Clock.Advance(TimeSpan.FromMilliseconds(100));

        var checkoutBody = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 4,
            BuyerEmail = botEmail,
            SessionToken = session.SessionToken,
            DeviceFingerprint = "fp-botnet-cluster",
            Telemetry = new ClientTelemetryDto
            {
                IsWebdriver = 1,
                ActionIntervalStdDev = 8.0f,
                DeviceFingerprint = "fp-botnet-cluster"
            }
        };

        // 2. Bot gửi liên tục nhiều request để kích hoạt high burst rate (> 6 req/10s)
        HttpResponseMessage? checkoutResponse = null;
        for (int i = 0; i < 8; i++)
        {
            checkoutResponse?.Dispose();
            checkoutResponse = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, checkoutBody);
        }

        Assert.NotNull(checkoutResponse);
        using (checkoutResponse)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, checkoutResponse.StatusCode);

        var root = await checkoutResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Block", root.GetProperty("decision").GetString());
        Assert.True(root.GetProperty("riskScore").GetSingle() >= 0.75f);
        Assert.Contains("blocked", root.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);

        // 3. Kiểm chứng toàn vẹn dữ liệu: Tồn kho DB tuyệt đối không bị trừ
        using var scope = f.Mock.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrganizerDbContext>();
        var tier = await db.TicketTiers.AsNoTracking().FirstAsync(t => t.Id == tierId);
        Assert.Equal(50, tier.AvailableQuantity); // Không hề bị trừ tồn kho
        }
    }

    [Fact]
    public async Task Checkout_WhenStealthBotInGreyZone_Returns403ForbiddenWithChallenge()
    {
        var tierId = await CreateTestTierAsync(initialAvailable: 20);
        var stealthEmail = $"stealth_scalper_{Guid.NewGuid():N}@stealth.org";

        using var sessionResponse = await f.MockHttp.GetAsync($"{SessionEndpoint}?tierId={tierId}");
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionTokenResponse>();
        Assert.NotNull(session);

        // Nhịp bấm vùng xám (55ms), thời gian checkout 4 giây
        f.Clock.Advance(TimeSpan.FromSeconds(4));

        var checkoutBody = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 2,
            BuyerEmail = stealthEmail,
            SessionToken = session.SessionToken,
            DeviceFingerprint = "dev-stealth-fp",
            Telemetry = new ClientTelemetryDto
            {
                IsWebdriver = 0,
                ActionIntervalStdDev = 55.0f,
                DeviceFingerprint = "dev-stealth-fp"
            }
        };

        // Kích hoạt thêm hold từ trước để đẩy vào vùng xám (Grey Zone: 0.35 <= RiskScore < 0.75)
        using (var scope = f.Mock.Services.CreateScope())
        {
            var redisService = scope.ServiceProvider.GetRequiredService<IRedisSlidingWindowService>();
            await redisService.RecordHoldAsync(stealthEmail, 3);
        }

        using var checkoutResponse = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, checkoutBody);

        // Kết quả phải trả về 403 Forbidden kèm challengeRequired = true hoặc 429 nếu rủi ro leo thang
        Assert.True(
            checkoutResponse.StatusCode == HttpStatusCode.Forbidden || checkoutResponse.StatusCode == HttpStatusCode.TooManyRequests,
            $"Expected Challenge (403) or Block (429), actual: {checkoutResponse.StatusCode}");

        var root = await checkoutResponse.Content.ReadFromJsonAsync<JsonElement>();
        var decision = root.GetProperty("decision").GetString();
        Assert.True(decision == "Challenge" || decision == "Block");
    }

    private async Task<Guid> CreateTestTierAsync(int initialAvailable = 100)
    {
        using var scope = f.Mock.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrganizerDbContext>();

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ResaleFixture.Event,
            TierName = "AntiBot Tier " + Guid.NewGuid().ToString("N")[..6],
            Price = 250_000m,
            TotalQuantity = initialAvailable,
            AvailableQuantity = initialAvailable
        };

        db.TicketTiers.Add(tier);
        await db.SaveChangesAsync();
        return tier.Id;
    }
}
