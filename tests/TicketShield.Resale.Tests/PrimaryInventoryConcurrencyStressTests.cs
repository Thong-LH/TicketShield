using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MockOrganizer.API.Controllers;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// SCRUM-227 / TEST-7.1.3: Overselling Concurrency Stress Tests for Primary Ticket Inventory.
/// Giả lập 50 request cùng mua 5 vé cuối cùng để kiểm chứng cơ chế Optimistic Concurrency Control
/// (PostgreSQL xmin + DB Transaction + Retry back-off) đảm bảo tuyệt đối không bị âm kho vé.
/// </summary>
public sealed class PrimaryInventoryConcurrencyStressTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    private const string CheckoutEndpoint = "api/organizer/orders/checkout";

    [Fact]
    public async Task Simultaneous_50_requests_competing_for_last_5_tickets_never_causes_overselling()
    {
        // 1. Arrange: Khởi tạo hạng vé với đúng 5 vé cuối cùng trong kho
        const int initialStock = 5;
        const int concurrentRequests = 50;
        var tierId = await CreateTestTierAsync(initialAvailable: initialStock);

        // 2. Act: Bắn đồng thời 50 HTTP requests tranh mua 5 vé cuối cùng
        var checkoutTasks = Enumerable.Range(1, concurrentRequests).Select(async i =>
        {
            var body = new CheckoutRequest
            {
                TierId = tierId,
                Quantity = 1,
                BuyerEmail = $"stress_buyer_{i}_{Guid.NewGuid():N}@ticketshield.vn"
            };

            using var response = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, body);
            return response.StatusCode;
        });

        var results = await Task.WhenAll(checkoutTasks);

        // 3. Assert: Kiểm chứng tính toàn vẹn dữ liệu và ngăn chặn bán khống (No Overselling)
        var okCount = results.Count(s => s == HttpStatusCode.OK);
        var conflictCount = results.Count(s => s == HttpStatusCode.Conflict);
        var busyCount = results.Count(s => s == HttpStatusCode.ServiceUnavailable);

        // Không có bất kỳ lỗi 500 nào xảy ra
        Assert.DoesNotContain(results, s => s == HttpStatusCode.InternalServerError);

        // Mọi request chỉ được trả về 200 OK, 409 Conflict (hết vé), hoặc 503 (contention race limit)
        Assert.Equal(concurrentRequests, okCount + conflictCount + busyCount);

        // Đọc trạng thái cuối cùng trực tiếp từ database
        var finalAvailable = await GetAvailableQuantityAsync(tierId);
        var totalTicketsSold = await GetTotalTicketsSoldAsync(tierId);
        var totalOrdersCreated = await GetOrdersCountAsync(tierId);

        // BẢO CHỨNG QUAN TRỌNG NHẤT: Kho vé tuyệt đối không bao giờ bị âm (< 0)
        Assert.True(finalAvailable >= 0, $"Kho vé bị âm! AvailableQuantity = {finalAvailable}");

        // Tổng số vé bán thành công trong DB không được vượt quá số lượng 5 vé ban đầu
        Assert.True(totalTicketsSold <= initialStock, $"Bán vượt quá kho! Total sold = {totalTicketsSold}, Initial = {initialStock}");

        // Bảo toàn tổng số vé: (Số vé đã bán) + (Số vé còn lại trong kho) == Số vé ban đầu (5)
        Assert.Equal(initialStock, totalTicketsSold + finalAvailable);

        // Số đơn hàng thành công trong DB phải khớp đúng với số response 200 OK
        Assert.Equal(okCount, totalOrdersCreated);
        Assert.Equal(okCount, totalTicketsSold);
    }

    [Fact]
    public async Task Concurrent_requests_with_retry_on_busy_fully_sellout_all_5_tickets_without_overselling()
    {
        // 1. Arrange: Khởi tạo hạng vé có 5 vé cuối cùng
        const int initialStock = 5;
        const int concurrentRequests = 50;
        var tierId = await CreateTestTierAsync(initialAvailable: initialStock);

        // 2. Act: 50 requests đồng thời cạnh tranh, nếu gặp 503 (Server Busy) thì client retry
        // để mô phỏng hành vi client thực tế cho đến khi đạt trạng thái kết thúc (200 OK hoặc 409 Conflict)
        var checkoutTasks = Enumerable.Range(1, concurrentRequests).Select(async i =>
        {
            var body = new CheckoutRequest
            {
                TierId = tierId,
                Quantity = 1,
                BuyerEmail = $"retry_buyer_{i}_{Guid.NewGuid():N}@ticketshield.vn"
            };

            for (var retry = 0; retry < 10; retry++)
            {
                using var response = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, body);
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    // Contention backoff nhẹ trước khi thử lại
                    await Task.Delay(Random.Shared.Next(20, 60));
                    continue;
                }
                return response.StatusCode;
            }

            return HttpStatusCode.ServiceUnavailable;
        });

        var results = await Task.WhenAll(checkoutTasks);

        var okCount = results.Count(s => s == HttpStatusCode.OK);
        var conflictCount = results.Count(s => s == HttpStatusCode.Conflict);

        // 3. Assert:
        // Đúng chính xác 5 vé được bán thành công (HTTP 200)
        Assert.Equal(initialStock, okCount);

        // Các request còn lại nhận 409 Conflict (hoặc 503 nếu hết lượt retry)
        Assert.True(conflictCount >= concurrentRequests - initialStock - 5,
            $"Số lượng 409 Conflict phải xấp xỉ {concurrentRequests - initialStock}, thực tế: {conflictCount}");

        // Kiểm tra trực tiếp Database
        var finalAvailable = await GetAvailableQuantityAsync(tierId);
        var totalTicketsSold = await GetTotalTicketsSoldAsync(tierId);
        var totalOrders = await GetOrdersCountAsync(tierId);

        // Kho vé phải về đúng chính xác 0 (sạch kho, không âm)
        Assert.Equal(0, finalAvailable);

        // Bảng mock_orders ghi nhận đúng 5 đơn hàng với tổng quantity = 5
        Assert.Equal(initialStock, totalOrders);
        Assert.Equal(initialStock, totalTicketsSold);
    }

    [Fact]
    public async Task Multi_ticket_concurrency_requests_prevent_overselling()
    {
        // 1. Arrange: Kho còn đúng 5 vé, 10 request đồng thời nhảy vào mua mỗi người 2 vé
        // Cần tối đa 2 người mua được (2 * 2 = 4 vé), người thứ 3 đòi 2 vé sẽ bị chặn vì chỉ còn 1 vé
        const int initialStock = 5;
        const int requestedPerOrder = 2;
        var tierId = await CreateTestTierAsync(initialAvailable: initialStock);

        var tasks = Enumerable.Range(1, 10).Select(async i =>
        {
            var body = new CheckoutRequest
            {
                TierId = tierId,
                Quantity = requestedPerOrder,
                BuyerEmail = $"multi_buyer_{i}_{Guid.NewGuid():N}@ticketshield.vn"
            };

            using var response = await f.MockHttp.PostAsJsonAsync(CheckoutEndpoint, body);
            return response.StatusCode;
        });

        var results = await Task.WhenAll(tasks);

        var finalAvailable = await GetAvailableQuantityAsync(tierId);
        var totalSold = await GetTotalTicketsSoldAsync(tierId);

        // Kho vé không bao giờ âm (phải còn ít nhất 1 vé)
        Assert.True(finalAvailable >= 0, $"Kho vé bị âm: {finalAvailable}");
        Assert.True(totalSold <= initialStock, $"Bán vượt quá kho: {totalSold} > {initialStock}");
        Assert.Equal(initialStock, totalSold + finalAvailable);

        // Tối đa 2 đơn hàng thành công (vì 2 * 2 = 4 <= 5, đơn thứ 3 cần 2 nhưng chỉ còn 1 nên phải bị chặn)
        var okOrders = await GetOrdersCountAsync(tierId);
        Assert.True(okOrders <= 2, $"Số đơn hàng vượt quá khả năng cung ứng: {okOrders}");
    }

    // =========================================================================
    // Database Helpers (Scoped trực tiếp vào DB PostgreSQL của MockOrganizer)
    // =========================================================================

    private async Task<Guid> CreateTestTierAsync(int initialAvailable)
    {
        using var scope = f.Mock.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrganizerDbContext>();

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = "stress-test-" + Guid.NewGuid().ToString("N")[..8],
            TierName = "Concurrency Stress Tier",
            Price = 250_000m,
            TotalQuantity = 100,
            AvailableQuantity = initialAvailable,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.TicketTiers.Add(tier);
        await db.SaveChangesAsync();
        return tier.Id;
    }

    private async Task<int> GetAvailableQuantityAsync(Guid tierId)
    {
        using var scope = f.Mock.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrganizerDbContext>();
        var tier = await db.TicketTiers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tierId);
        return tier?.AvailableQuantity ?? -1;
    }

    private async Task<int> GetOrdersCountAsync(Guid tierId)
    {
        using var scope = f.Mock.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrganizerDbContext>();
        return await db.MockOrders.AsNoTracking().CountAsync(o => o.TierId == tierId);
    }

    private async Task<int> GetTotalTicketsSoldAsync(Guid tierId)
    {
        using var scope = f.Mock.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrganizerDbContext>();
        return await db.MockOrders
            .AsNoTracking()
            .Where(o => o.TierId == tierId)
            .SumAsync(o => o.Quantity);
    }
}
