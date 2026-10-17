using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MockOrganizer.API.Controllers;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;
using MockOrganizer.API.Models;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// End-to-End Integration Tests for BE-PIPE-8.2.1:
/// Server Transaction Latency Measurement (time_to_checkout) and Concurrent Hold Lookup.
/// Verifies HTTP Session token generation, HMAC verification, server-side checkout latency calculation,
/// DB concurrent hold aggregation, and SLA execution performance.
/// </summary>
public sealed class ServerTransactionTelemetryIntegrationTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    private const string SessionEndpoint = "api/organizer/orders/session";
    private const string CheckoutEndpoint = "api/organizer/orders/checkout";

    [Fact]
    public async Task CreateSession_WithValidTierId_ReturnsSignedSessionToken()
    {
        var tierId = await CreateTestTierAsync(initialAvailable: 10);

        using var response = await f.MockHttp.GetAsync($"{SessionEndpoint}?tierId={tierId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SessionTokenResponse>();
        Assert.NotNull(result);
        Assert.Equal(tierId, result.TierId);
        Assert.False(string.IsNullOrWhiteSpace(result.SessionToken));
        Assert.Contains(".", result.SessionToken);
        Assert.Equal(7200, result.ExpiresInSeconds);
    }

    [Fact]
    public async Task CreateSession_WithEmptyTierId_ReturnsBadRequest()
    {
        using var response = await f.MockHttp.GetAsync($"{SessionEndpoint}?tierId={Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_WithValidSessionToken_CalculatesTimeToCheckoutAndMeasuresSLA()
    {
        var tierId = await CreateTestTierAsync(initialAvailable: 10);
        var buyerEmail = $"telemetry_buyer_{Guid.NewGuid():N}@ticketshield.vn";

        // 1. Lấy session token từ server
        using var sessionResponse = await f.MockHttp.GetAsync($"{SessionEndpoint}?tierId={tierId}");
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionTokenResponse>();
        Assert.NotNull(session);

        // Warm up ASP.NET route tree, EF query cache, and connection pool
        var warmUpBody = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 1,
            BuyerEmail = "warmup@ticketshield.vn",
            SessionToken = session.SessionToken
        };
        using var warmUpResponse = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, warmUpBody);

        // Giả lập người dùng xem trang và thao tác trong 2.5 giây trước khi ấn Checkout
        f.Clock.Advance(TimeSpan.FromSeconds(2.5));

        // 2. Gửi request checkout kèm SessionToken
        var checkoutBody = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 2,
            BuyerEmail = buyerEmail,
            SessionToken = session.SessionToken
        };

        using var checkoutResponse = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, checkoutBody);
        Assert.Equal(HttpStatusCode.OK, checkoutResponse.StatusCode);

        var root = await checkoutResponse.Content.ReadFromJsonAsync<JsonElement>();
        var telemetry = root.GetProperty("telemetry");

        var timeToCheckout = telemetry.GetProperty("timeToCheckoutSeconds").GetDouble();
        var holdCount = telemetry.GetProperty("concurrentHoldCount").GetInt32();
        var durationMs = telemetry.GetProperty("executionDurationMs").GetDouble();
        var isValid = telemetry.GetProperty("isSessionValid").GetBoolean();

        Assert.True(isValid);
        Assert.InRange(timeToCheckout, 2.4, 2.6);
        Assert.Equal(0, holdCount);
        Assert.True(durationMs < 50.0, $"Telemetry measurement latency {durationMs}ms exceeded threshold");
    }

    [Fact]
    public async Task Checkout_WithExistingPendingOrders_AggregatesConcurrentHoldCorrectly()
    {
        var tierId = await CreateTestTierAsync(initialAvailable: 20);
        var buyerEmail = $"hold_buyer_{Guid.NewGuid():N}@ticketshield.vn";
        var otherBuyer = $"other_{Guid.NewGuid():N}@ticketshield.vn";
        var now = DateTimeOffset.UtcNow;

        // Chuẩn bị dữ liệu trong PostgreSQL DB
        using (var scope = f.Mock.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrganizerDbContext>();

            // 1. Đơn PENDING của buyer (3 vé, tạo 1 phút trước) -> TÍNH
            db.MockOrders.Add(new MockOrder
            {
                Id = Guid.NewGuid(),
                TierId = tierId,
                EventId = "ev-test",
                TierName = "GA",
                Quantity = 3,
                UnitPrice = 100_000m,
                TotalAmount = 300_000m,
                Status = "PENDING",
                BuyerEmail = buyerEmail,
                CreatedAt = now.AddMinutes(-1)
            });

            // 2. Đơn PENDING thứ 2 của buyer (4 vé, tạo 4 phút trước) -> TÍNH
            db.MockOrders.Add(new MockOrder
            {
                Id = Guid.NewGuid(),
                TierId = tierId,
                EventId = "ev-test",
                TierName = "GA",
                Quantity = 4,
                UnitPrice = 100_000m,
                TotalAmount = 400_000m,
                Status = "PENDING",
                BuyerEmail = buyerEmail,
                CreatedAt = now.AddMinutes(-4)
            });

            // 3. Đơn CONFIRMED của buyer (5 vé) -> KHÔNG TÍNH
            db.MockOrders.Add(new MockOrder
            {
                Id = Guid.NewGuid(),
                TierId = tierId,
                EventId = "ev-test",
                TierName = "GA",
                Quantity = 5,
                UnitPrice = 100_000m,
                TotalAmount = 500_000m,
                Status = "CONFIRMED",
                BuyerEmail = buyerEmail,
                CreatedAt = now.AddMinutes(-2)
            });

            // 4. Đơn PENDING nhưng hết hạn (15 phút trước) -> KHÔNG TÍNH
            db.MockOrders.Add(new MockOrder
            {
                Id = Guid.NewGuid(),
                TierId = tierId,
                EventId = "ev-test",
                TierName = "GA",
                Quantity = 10,
                UnitPrice = 100_000m,
                TotalAmount = 1_000_000m,
                Status = "PENDING",
                BuyerEmail = buyerEmail,
                CreatedAt = now.AddMinutes(-15)
            });

            // 5. Đơn PENDING của người khác (8 vé) -> KHÔNG TÍNH
            db.MockOrders.Add(new MockOrder
            {
                Id = Guid.NewGuid(),
                TierId = tierId,
                EventId = "ev-test",
                TierName = "GA",
                Quantity = 8,
                UnitPrice = 100_000m,
                TotalAmount = 800_000m,
                Status = "PENDING",
                BuyerEmail = otherBuyer,
                CreatedAt = now.AddMinutes(-1)
            });

            await db.SaveChangesAsync();
        }

        // Thực hiện Checkout
        var checkoutBody = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 1,
            BuyerEmail = buyerEmail
        };

        using var checkoutResponse = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, checkoutBody);
        Assert.True(checkoutResponse.StatusCode == HttpStatusCode.OK || checkoutResponse.StatusCode == HttpStatusCode.TooManyRequests);

        var root = await checkoutResponse.Content.ReadFromJsonAsync<JsonElement>();
        var telemetry = root.GetProperty("telemetry");

        // 3 vé + 4 vé = 7 vé
        Assert.Equal(7, telemetry.GetProperty("concurrentHoldCount").GetInt32());
    }

    [Fact]
    public async Task Checkout_WithoutSessionTokenOrWithTamperedToken_ReportsInvalidAndNegativeOneLatency()
    {
        var tierId = await CreateTestTierAsync(initialAvailable: 10);

        // Trường hợp 1: Không truyền session token (null)
        var bodyWithoutToken = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 1,
            BuyerEmail = "null_token@ticketshield.vn",
            SessionToken = null
        };

        using var response1 = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, bodyWithoutToken);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var root1 = await response1.Content.ReadFromJsonAsync<JsonElement>();
        var telemetry1 = root1.GetProperty("telemetry");
        Assert.False(telemetry1.GetProperty("isSessionValid").GetBoolean());
        Assert.Equal(-1.0, telemetry1.GetProperty("timeToCheckoutSeconds").GetDouble());

        // Trường hợp 2: Token giả mạo/tampered
        var bodyTampered = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 1,
            BuyerEmail = "tampered_token@ticketshield.vn",
            SessionToken = "header.tamperedPayload.invalidSignature"
        };

        using var response2 = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, bodyTampered);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var root2 = await response2.Content.ReadFromJsonAsync<JsonElement>();
        var telemetry2 = root2.GetProperty("telemetry");
        Assert.False(telemetry2.GetProperty("isSessionValid").GetBoolean());
        Assert.Equal(-1.0, telemetry2.GetProperty("timeToCheckoutSeconds").GetDouble());
    }

    [Fact]
    public async Task Checkout_WithDeviceFingerprint_ComputesBurstRateAndDeviceOverlap()
    {
        var tierId = await CreateTestTierAsync(initialAvailable: 10);
        var buyerEmail = $"device_user_{Guid.NewGuid():N}@ticketshield.vn";
        var deviceFp = "fp_" + Guid.NewGuid().ToString("N");

        using var sessionResponse = await f.MockHttp.GetAsync($"{SessionEndpoint}?tierId={tierId}");
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionTokenResponse>();
        Assert.NotNull(session);

        f.Clock.Advance(TimeSpan.FromSeconds(15));

        var body = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 1,
            BuyerEmail = buyerEmail,
            SessionToken = session.SessionToken,
            DeviceFingerprint = deviceFp
        };

        using var response = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await response.Content.ReadFromJsonAsync<JsonElement>();
        var telemetry = root.GetProperty("telemetry");

        Assert.True(telemetry.GetProperty("burstRate10s").GetInt32() >= 1);
        Assert.Equal(1, telemetry.GetProperty("deviceAccountOverlap").GetInt32());
        Assert.True(telemetry.GetProperty("redisDurationMs").GetDouble() >= 0.0);
    }

    [Fact]
    public async Task Checkout_MultipleAccountsOnSameDevice_IncrementsDeviceOverlap()
    {
        var tierId = await CreateTestTierAsync(initialAvailable: 10);
        var sharedDevice = "fp_shared_" + Guid.NewGuid().ToString("N");
        var buyer1 = $"shared_user1_{Guid.NewGuid():N}@ticketshield.vn";
        var buyer2 = $"shared_user2_{Guid.NewGuid():N}@ticketshield.vn";

        using var sessionResponse = await f.MockHttp.GetAsync($"{SessionEndpoint}?tierId={tierId}");
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionTokenResponse>();
        Assert.NotNull(session);

        f.Clock.Advance(TimeSpan.FromSeconds(15));

        // Lần mua 1 từ buyer 1
        var body1 = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 1,
            BuyerEmail = buyer1,
            SessionToken = session.SessionToken,
            DeviceFingerprint = sharedDevice
        };
        using var resp1 = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, body1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        var root1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, root1.GetProperty("telemetry").GetProperty("deviceAccountOverlap").GetInt32());

        f.Clock.Advance(TimeSpan.FromSeconds(15));
        using var sessionResponse2 = await f.MockHttp.GetAsync($"{SessionEndpoint}?tierId={tierId}");
        var session2 = await sessionResponse2.Content.ReadFromJsonAsync<SessionTokenResponse>();
        Assert.NotNull(session2);
        f.Clock.Advance(TimeSpan.FromSeconds(15));

        // Lần mua 2 từ buyer 2 trên cùng thiết bị
        var body2 = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 1,
            BuyerEmail = buyer2,
            SessionToken = session2.SessionToken,
            DeviceFingerprint = sharedDevice
        };
        using var resp2 = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, body2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        var root2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();

        // Tương quan thiết bị tăng lên 2
        Assert.Equal(2, root2.GetProperty("telemetry").GetProperty("deviceAccountOverlap").GetInt32());
    }

    [Fact]
    public async Task Checkout_WithoutDeviceFingerprint_ReturnsNegativeOneOverlap()
    {
        var tierId = await CreateTestTierAsync(initialAvailable: 10);
        var buyerEmail = $"nodev_user_{Guid.NewGuid():N}@ticketshield.vn";

        using var sessionResponse = await f.MockHttp.GetAsync($"{SessionEndpoint}?tierId={tierId}");
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionTokenResponse>();
        Assert.NotNull(session);

        f.Clock.Advance(TimeSpan.FromSeconds(15));

        var body = new CheckoutRequest
        {
            TierId = tierId,
            Quantity = 1,
            BuyerEmail = buyerEmail,
            SessionToken = session.SessionToken,
            DeviceFingerprint = null,
            Telemetry = null
        };

        using var response = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await response.Content.ReadFromJsonAsync<JsonElement>();
        var telemetry = root.GetProperty("telemetry");

        // Spec 08: Missing device_fingerprint -> -1
        Assert.Equal(-1, telemetry.GetProperty("deviceAccountOverlap").GetInt32());
        Assert.True(telemetry.GetProperty("burstRate10s").GetInt32() >= 1);
    }

    private async Task<Guid> CreateTestTierAsync(int initialAvailable)
    {
        using var scope = f.Mock.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrganizerDbContext>();

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = "telemetry-test-" + Guid.NewGuid().ToString("N")[..8],
            TierName = "Telemetry Test Tier",
            Price = 150_000m,
            TotalQuantity = 100,
            AvailableQuantity = initialAvailable,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.TicketTiers.Add(tier);
        await db.SaveChangesAsync();
        return tier.Id;
    }
}
