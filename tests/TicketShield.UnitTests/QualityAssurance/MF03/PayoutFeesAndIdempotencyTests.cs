using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using TicketShield.Settlement.API.Data;
using TicketShield.Settlement.API.Gateway;
using TicketShield.Settlement.API.Payouts;
using Xunit;
using PayoutIdempotency = TicketShield.Application.Common.Models.PayoutIdempotency;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// TEST-5.5.1: Targeted Unit Tests for Payout Fees and Idempotency
/// Description: Bộ Unit Tests kiểm tra tính toán phí sàn (Seller Fee), làm tròn tiền Net Payout và kiểm soát Idempotency.
/// [Phụ thuộc: sau EXT-SETTLE-5.4.1 | Mở khóa cho: TEST-5.5.2]
///
/// Các trụ cột kiểm thử:
/// 1. Payout Fees (Seller Fee): Tính toán phí theo tỷ lệ %, kiểm soát trần/sàn tối thiểu (MinimumSellerFee), chặn niêm yết vé dưới phí sàn, mô hình phí đồng nhất (Uniform Fee) Public vs Private.
/// 2. Net Payout Rounding & Arithmetic Conservation: Làm tròn tiền VND nguyên vẹn không số thập phân, bảo toàn số học 100% (ResalePrice = NetSellerPayout + SellerFee, không thất thoát 1 xu), Snapshot bảo tồn.
/// 3. Payout Idempotency (BR-E06): Định dạng Idempotency Key duy nhất, chống giải ngân trùng (Replay Protection), chống race condition song song, Retry exponential backoff và Atomic CAS Lock.
/// </summary>
public class PayoutFeesAndIdempotencyTests
{
    #region Fixture & Factory Helpers

    private static DynamicResaleFeeCalculator CreateCalculator(ResaleFeeConfig? config = null, IMemoryCache? memoryCache = null)
    {
        var cfg = config ?? new ResaleFeeConfig
        {
            BuyerFeePercentage = 0.05m,
            SellerFeePercentage = 0.03m,
            MinimumBuyerFee = 10_000m,
            MinimumSellerFee = 5_000m
        };

        var mockRepo = new Mock<ISystemSettingRepository>();
        mockRepo.Setup(r => r.GetResaleFeeConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(cfg);

        var cache = memoryCache ?? new MemoryCache(new MemoryCacheOptions());
        return new DynamicResaleFeeCalculator(mockRepo.Object, cache);
    }

    private static async Task<SettlementDbContext> CreateSettlementDbAsync()
    {
        var options = new DbContextOptionsBuilder<SettlementDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new SettlementDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static SettlementPayoutService CreateSettlementService(
        SettlementDbContext db,
        ISettlementGateway gateway,
        ICorePayoutReporter? reporter = null,
        TimeSpan? firstWait = null)
    {
        var rep = reporter ?? new StubCorePayoutReporter();
        return new SettlementPayoutService(db, gateway, rep, firstWait ?? TimeSpan.FromSeconds(1));
    }

    private sealed class StubCorePayoutReporter : ICorePayoutReporter
    {
        public Task<bool> ReportAsync(Guid escrowId, string idempotencyKey, bool succeeded, string? bankReference, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private static (SqliteConnection conn, TicketShieldDbContext db) CreateSqliteCoreDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new TicketShieldDbContext(options);
        db.Database.EnsureCreated();
        return (connection, db);
    }

    private static async Task<Guid> SeedCoreEscrowAsync(TicketShieldDbContext db, decimal resalePrice, decimal sellerFee, EscrowStatus status)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var org = new Organizer { Id = Guid.NewGuid(), Name = "Organizer", OfficialEmail = $"org-{suffix}@test.local" };
        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = $"seller-{suffix}@test.local",
            FullName = "Nguyen Van Ban",
            PayoutBankCode = "MB",
            PayoutAccountNumber = "0938434102",
            PayoutAccountName = "NGUYEN VAN BAN"
        };
        var buyer = new ShadowUser { Id = Guid.NewGuid(), Email = $"buyer-{suffix}@test.local", FullName = "Le Thi Mua" };
        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "Concert 2026",
            Venue = "My Dinh",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(7),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(6)
        };
        var tier = new TicketTier { Id = Guid.NewGuid(), EventId = ev.Id, TierName = "VIP", OriginalPrice = resalePrice };
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = $"TICK-{suffix}",
            OriginalPrice = resalePrice,
            ResalePrice = resalePrice,
            ListingStatus = ListingStatus.Sold
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = resalePrice,
            BuyerFee = Math.Round(resalePrice * 0.05m, 0),
            SellerFee = sellerFee,
            TotalBuyerPaid = resalePrice + Math.Round(resalePrice * 0.05m, 0),
            NetSellerPayout = resalePrice - sellerFee,
            Status = status,
            InSettlementBuffer = true
        };

        db.Organizers.Add(org);
        db.ShadowUsers.AddRange(seller, buyer);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return escrow.Id;
    }

    #endregion

    #region Pillar 1: Payout Fees (Seller Fee Calculation & Minimum Thresholds)

    [Theory]
    [InlineData(500_000, 0.03, 15_000)]
    [InlineData(1_000_000, 0.03, 30_000)]
    [InlineData(2_500_000, 0.04, 100_000)]
    [InlineData(10_000_000, 0.02, 200_000)]
    public async Task SellerFee_StandardPercentage_CalculatesExactFee(
        decimal resalePrice, decimal sellerPercentage, decimal expectedSellerFee)
    {
        // Arrange
        var config = new ResaleFeeConfig
        {
            BuyerFeePercentage = 0.05m,
            SellerFeePercentage = sellerPercentage,
            MinimumBuyerFee = 10_000m,
            MinimumSellerFee = 5_000m
        };
        var calculator = CreateCalculator(config);

        // Act
        var result = await calculator.CalculateFeeAsync(resalePrice, isPrivate: false);

        // Assert
        Assert.Equal(expectedSellerFee, result.SellerFee);
        Assert.Equal(resalePrice - expectedSellerFee, result.NetSellerPayout);
    }

    [Theory]
    [InlineData(50_000)]  // 50.000 * 3% = 1.500 < 5.000
    [InlineData(100_000)] // 100.000 * 3% = 3.000 < 5.000
    [InlineData(150_000)] // 150.000 * 3% = 4.500 < 5.000
    public async Task SellerFee_LowTicketPrice_ClampsToMinimumSellerFeeThreshold(decimal lowResalePrice)
    {
        // Arrange
        var config = new ResaleFeeConfig
        {
            SellerFeePercentage = 0.03m,
            MinimumSellerFee = 5_000m
        };
        var calculator = CreateCalculator(config);

        // Act
        var result = await calculator.CalculateFeeAsync(lowResalePrice, isPrivate: false);

        // Assert - Phí người bán được kẹp sàn tối thiểu 5.000 VNĐ
        Assert.Equal(5_000m, result.SellerFee);
        Assert.Equal(lowResalePrice - 5_000m, result.NetSellerPayout);
    }

    [Fact]
    public async Task SellerFee_ResalePriceBelowMinimumFee_ThrowsBusinessRuleViolationException()
    {
        // Arrange - Giá bán lại thấp hơn phí sàn tối thiểu (ví dụ: 4.000đ < 5.000đ)
        var config = new ResaleFeeConfig
        {
            MinimumSellerFee = 5_000m
        };
        var calculator = CreateCalculator(config);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            calculator.CalculateFeeAsync(4_000m, isPrivate: false));

        Assert.Contains("phải lớn hơn hoặc bằng phí sàn tối thiểu", ex.Message);
    }

    [Theory]
    [InlineData(200_000)]
    [InlineData(750_000)]
    [InlineData(3_000_000)]
    public async Task SellerFee_UniformModel_PublicAndPrivateResaleYieldIdenticalSellerFee(decimal resalePrice)
    {
        // Arrange
        var calculator = CreateCalculator();

        // Act - Tính phí cho cả kênh Public Marketplace và kênh Private Link
        var publicResult = await calculator.CalculateFeeAsync(resalePrice, isPrivate: false);
        var privateResult = await calculator.CalculateFeeAsync(resalePrice, isPrivate: true);

        // Assert - Biểu phí đồng nhất 100% giữa Public và Private
        Assert.Equal(publicResult.SellerFee, privateResult.SellerFee);
        Assert.Equal(publicResult.NetSellerPayout, privateResult.NetSellerPayout);
        Assert.Equal(publicResult.BuyerFee, privateResult.BuyerFee);
        Assert.Equal(publicResult.TotalBuyerPaid, privateResult.TotalBuyerPaid);
    }

    [Fact]
    public async Task SellerFee_DynamicAdminConfigChange_InvalidatesCacheAndAppliesNewRates()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mockRepo = new Mock<ISystemSettingRepository>();

        var oldConfig = new ResaleFeeConfig
        {
            SellerFeePercentage = 0.03m,
            MinimumSellerFee = 5_000m
        };
        var newConfig = new ResaleFeeConfig
        {
            SellerFeePercentage = 0.05m,
            MinimumSellerFee = 10_000m
        };

        mockRepo.SetupSequence(r => r.GetResaleFeeConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldConfig)
            .ReturnsAsync(newConfig);

        var calculator = new DynamicResaleFeeCalculator(mockRepo.Object, cache);
        decimal price = 1_000_000m;

        // Act 1: Lần đầu tiên tính theo cấu hình cũ (3% = 30.000đ)
        var result1 = await calculator.CalculateFeeAsync(price, isPrivate: false);
        Assert.Equal(30_000m, result1.SellerFee);

        // Lần 2 (khi chưa xóa cache): vẫn lấy cache cũ
        var cachedResult = await calculator.CalculateFeeAsync(price, isPrivate: false);
        Assert.Equal(30_000m, cachedResult.SellerFee);

        // Act 2: Admin cập nhật cấu hình và xoá cache (InvalidateCache)
        calculator.InvalidateCache();
        var result2 = await calculator.CalculateFeeAsync(price, isPrivate: false);

        // Assert - Phí sàn mới được áp dụng (5% = 50.000đ)
        Assert.Equal(50_000m, result2.SellerFee);
        Assert.Equal(950_000m, result2.NetSellerPayout);
    }

    #endregion

    #region Pillar 2: Net Payout Rounding & Financial Arithmetic Conservation

    [Theory]
    [InlineData(1_234_567, 0.03)] // 1.234.567 * 0.03 = 37.037,01 -> Round = 37.037
    [InlineData(888_888, 0.035)]  // 888.888 * 0.035 = 31.111,08 -> Round = 31.111
    [InlineData(333_333, 0.03)]   // 333.333 * 0.03 = 9.999,99 -> Round = 10.000
    [InlineData(9_999_999, 0.04)] // 9.999.999 * 0.04 = 399.999,96 -> Round = 400.000
    public async Task NetPayout_VndCurrencyNoFractionalDecimals_RoundsToWholeVndInteger(
        decimal resalePrice, decimal sellerPercentage)
    {
        // Arrange
        var config = new ResaleFeeConfig
        {
            SellerFeePercentage = sellerPercentage,
            MinimumSellerFee = 5_000m
        };
        var calculator = CreateCalculator(config);

        // Act
        var result = await calculator.CalculateFeeAsync(resalePrice, isPrivate: false);

        // Assert - Tiền VND không có số thập phân lẻ
        Assert.Equal(0, decimal.Remainder(result.SellerFee, 1m));
        Assert.Equal(0, decimal.Remainder(result.NetSellerPayout, 1m));
    }

    [Theory]
    [InlineData(50_000)]
    [InlineData(123_456)]
    [InlineData(500_000)]
    [InlineData(1_333_333)]
    [InlineData(2_500_000)]
    [InlineData(7_777_777)]
    public async Task NetPayout_ZeroLossArithmeticConservation_PreservesExactTotal(decimal resalePrice)
    {
        // Arrange
        var calculator = CreateCalculator();

        // Act
        var result = await calculator.CalculateFeeAsync(resalePrice, isPrivate: false);

        // Assert - Nguyên tắc bảo toàn số học tuyệt đối: NetSellerPayout + SellerFee == OriginalPrice
        var reconstructedPrice = result.NetSellerPayout + result.SellerFee;
        Assert.Equal(resalePrice, reconstructedPrice);
        Assert.Equal(0m, result.OriginalPrice - (result.NetSellerPayout + result.SellerFee));
    }

    [Fact]
    public async Task NetPayout_BoundaryCondition_ResalePriceEqualsMinimumSellerFee_NetPayoutIsZero()
    {
        // Arrange - Giá bán đúng bằng phí sàn tối thiểu (5.000đ)
        var config = new ResaleFeeConfig { MinimumSellerFee = 5_000m };
        var calculator = CreateCalculator(config);

        // Act
        var result = await calculator.CalculateFeeAsync(5_000m, isPrivate: false);

        // Assert - Seller nhận 0đ, không bị âm tiền
        Assert.Equal(5_000m, result.SellerFee);
        Assert.Equal(0m, result.NetSellerPayout);
    }

    [Fact]
    public async Task NetPayout_EscrowSnapshotPreservation_LockedEscrowPreservesOriginalNetPayoutOnAdminFeeChange()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mockRepo = new Mock<ISystemSettingRepository>();

        var initialConfig = new ResaleFeeConfig
        {
            SellerFeePercentage = 0.03m,
            MinimumSellerFee = 5_000m
        };
        mockRepo.Setup(r => r.GetResaleFeeConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialConfig);

        var calculator = new DynamicResaleFeeCalculator(mockRepo.Object, cache);
        decimal resalePrice = 2_000_000m;

        // Giao dịch checkout tạo Escrow (3% = 60.000đ, Net = 1.940.000đ)
        var calc1 = await calculator.CalculateFeeAsync(resalePrice, isPrivate: false);
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            OriginalTicketPrice = resalePrice,
            SellerFee = calc1.SellerFee,
            NetSellerPayout = calc1.NetSellerPayout,
            Status = EscrowStatus.Locked
        };

        // Act - Admin tăng phí sàn lên 8% và xóa cache
        var updatedConfig = new ResaleFeeConfig
        {
            SellerFeePercentage = 0.08m,
            MinimumSellerFee = 5_000m
        };
        mockRepo.Setup(r => r.GetResaleFeeConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedConfig);
        calculator.InvalidateCache();

        var calc2 = await calculator.CalculateFeeAsync(resalePrice, isPrivate: false);

        // Assert - Giao dịch cũ vẫn giữ nguyên snapshot 100%
        Assert.Equal(60_000m, escrow.SellerFee);
        Assert.Equal(1_940_000m, escrow.NetSellerPayout);

        // Giao dịch mới nhận biểu phí cập nhật (8% = 160.000đ, Net = 1.840.000đ)
        Assert.Equal(160_000m, calc2.SellerFee);
        Assert.Equal(1_840_000m, calc2.NetSellerPayout);
    }

    [Fact]
    public async Task NetPayout_MultiTicketBundle_FeeAndPayoutAggregationIntegrity()
    {
        // Arrange - Gói bundle gồm 3 vé với các mệnh giá khác nhau
        var ticketPrices = new[] { 300_000m, 550_000m, 1_200_000m };
        var calculator = CreateCalculator();

        // Act
        decimal totalResale = 0m;
        decimal totalSellerFee = 0m;
        decimal totalNetPayout = 0m;

        foreach (var price in ticketPrices)
        {
            var res = await calculator.CalculateFeeAsync(price, isPrivate: false);
            totalResale += price;
            totalSellerFee += res.SellerFee;
            totalNetPayout += res.NetSellerPayout;
        }

        // Assert
        Assert.Equal(2_050_000m, totalResale);
        Assert.Equal(totalResale, totalSellerFee + totalNetPayout);
        Assert.True(totalNetPayout > 0);
    }

    #endregion

    #region Pillar 3: Payout Idempotency & Gateway Protection (BR-E06)

    [Fact]
    public void PayoutIdempotency_DeterministicKeyGeneration_ProducesConsistentFormat()
    {
        // Arrange
        var escrowId = Guid.NewGuid();

        // Act
        var keyAttempt0 = PayoutIdempotency.Key(escrowId, 0);
        var keyAttempt1 = PayoutIdempotency.Key(escrowId, 1);
        var keyAttempt2 = PayoutIdempotency.Key(escrowId, 2);

        // Assert - Đúng chuẩn định dạng: IDEMP-{escrowId}-{retryCount}
        Assert.Equal($"IDEMP-{escrowId}-0", keyAttempt0);
        Assert.Equal($"IDEMP-{escrowId}-1", keyAttempt1);
        Assert.Equal($"IDEMP-{escrowId}-2", keyAttempt2);

        // Tính phân biệt giữa các lần retry
        Assert.NotEqual(keyAttempt0, keyAttempt1);
        Assert.NotEqual(keyAttempt1, keyAttempt2);
    }

    [Fact]
    public async Task PayoutIdempotency_DuplicateCommandWithSameKey_ReturnsExistingReceiptWithoutReCallingGateway()
    {
        // Arrange
        await using var db = await CreateSettlementDbAsync();
        var gateway = new MockNapasPayoutGateway();
        var service = CreateSettlementService(db, gateway);

        var escrowId = Guid.NewGuid();
        var command = new PayoutCommand
        {
            EscrowId = escrowId,
            SellerId = Guid.NewGuid(),
            Amount = 1_500_000m,
            RetryCount = 0,
            IdempotencyKey = PayoutIdempotency.Key(escrowId, 0),
            BankCode = "MB",
            AccountNumber = "0938434102",
            AccountName = "NGUYEN VAN BAN"
        };

        // Act 1: Lần gọi đầu tiên (chưa có trong DB) -> gọi gateway thật
        var firstResult = await service.AcceptAsync(command);

        // Assert 1
        Assert.True(firstResult.Accepted);
        Assert.True(firstResult.GatewayCalled);
        Assert.Equal(TransferState.Succeeded, firstResult.State);
        Assert.NotNull(firstResult.BankReference);

        // Act 2: Bắn lại lệnh thanh toán trùng lặp (Duplicate Webhook / Retry nhầm)
        var secondResult = await service.AcceptAsync(command);

        // Assert 2 - Idempotency bảo vệ: Trả kết quả cũ, TUYỆT ĐỐI KHÔNG gọi lại Gateway
        Assert.True(secondResult.Accepted);
        Assert.False(secondResult.GatewayCalled);
        Assert.Equal(TransferState.Succeeded, secondResult.State);
        Assert.Equal(firstResult.BankReference, secondResult.BankReference);

        // Kiểm tra Database: Chỉ có DUY NHẤT 1 bản ghi TransferOrder
        Assert.Equal(1, await db.TransferOrders.CountAsync(o => o.IdempotencyKey == command.IdempotencyKey));
    }

    [Fact]
    public async Task PayoutIdempotency_ReplayOfFailedOrder_ReturnsFailedWithoutInvokingGateway()
    {
        // Arrange - Giả lập lệnh có số tài khoản bị ngân hàng từ chối (Invalid Account)
        await using var db = await CreateSettlementDbAsync();
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.NewGuid();
        gateway.RejectAccount(escrowId);

        var service = CreateSettlementService(db, gateway);
        var command = new PayoutCommand
        {
            EscrowId = escrowId,
            SellerId = Guid.NewGuid(),
            Amount = 500_000m,
            RetryCount = 0,
            IdempotencyKey = PayoutIdempotency.Key(escrowId, 0),
            BankCode = "MB",
            AccountNumber = "0938434102",
            AccountName = "INVALID ACCOUNT"
        };

        // Act 1: Gọi lần 1 -> gateway từ chối, lưu State = Failed
        var first = await service.AcceptAsync(command);
        Assert.Equal(TransferState.Failed, first.State);

        // Act 2: Replay lại lệnh cũ
        var second = await service.AcceptAsync(command);

        // Assert - Trả về Failed ngay, không gọi lại gateway
        Assert.True(second.Accepted);
        Assert.Equal(TransferState.Failed, second.State);
        Assert.False(second.GatewayCalled);
    }

    [Fact]
    public async Task PayoutIdempotency_ConcurrentPayoutExecution_OnlySingleGatewayTransferExecuted()
    {
        // Arrange - Mô phỏng 5 luồng xử lý song song cùng 1 PayoutCommand
        await using var db = await CreateSettlementDbAsync();
        var gateway = new MockNapasPayoutGateway();
        var service = CreateSettlementService(db, gateway);

        var escrowId = Guid.NewGuid();
        var command = new PayoutCommand
        {
            EscrowId = escrowId,
            SellerId = Guid.NewGuid(),
            Amount = 1_000_000m,
            RetryCount = 0,
            IdempotencyKey = PayoutIdempotency.Key(escrowId, 0),
            BankCode = "MB",
            AccountNumber = "0938434102",
            AccountName = "SELLER BAN"
        };

        // Act - Chạy đồng thời 5 tasks
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => service.AcceptAsync(command))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert - Tất cả các lời gọi đều thành công và nhận cùng 1 mã đối soát ngân hàng
        Assert.All(results, r => Assert.True(r.Accepted));
        Assert.All(results, r => Assert.Equal(TransferState.Succeeded, r.State));

        var distinctBankRefs = results.Select(r => r.BankReference).Distinct().ToList();
        Assert.Single(distinctBankRefs);

        // Trong CSDL chỉ có duy nhất 1 bản ghi
        Assert.Equal(1, await db.TransferOrders.CountAsync(t => t.IdempotencyKey == command.IdempotencyKey));
    }

    [Fact]
    public async Task PayoutIdempotency_RetryWithIncrementedCount_UsesNewKeyAndTracksRetryNumber()
    {
        // Arrange - Lần 0 bị timeout, cần retry lần 1
        await using var db = await CreateSettlementDbAsync();
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.NewGuid();
        gateway.TimeoutNext(escrowId, 1); // Timeout lần đầu

        var service = CreateSettlementService(db, gateway, firstWait: TimeSpan.Zero);

        // Act 1: Lần thử ban đầu (RetryCount = 0)
        var cmd0 = new PayoutCommand
        {
            EscrowId = escrowId,
            SellerId = Guid.NewGuid(),
            Amount = 500_000m,
            RetryCount = 0,
            IdempotencyKey = PayoutIdempotency.Key(escrowId, 0),
            BankCode = "MB",
            AccountNumber = "0938434102",
            AccountName = "SELLER BAN"
        };
        var res0 = await service.AcceptAsync(cmd0);
        Assert.Equal(TransferState.Pending, res0.State);

        // Act 2: Worker retry với timeout đã hết -> Thành công
        await service.RetryDueAsync(DateTimeOffset.UtcNow.AddMinutes(5));

        var order = await db.TransferOrders.SingleAsync(o => o.IdempotencyKey == cmd0.IdempotencyKey);
        Assert.Equal(TransferState.Succeeded, order.State);
        Assert.NotNull(order.BankReference);
        Assert.Equal(2, order.AttemptCount);
    }

    [Fact]
    public async Task PayoutIdempotency_MaxAttemptsExhausted_HaltsRetriesAndTransitionsToExhausted()
    {
        // Arrange - Ngân hàng timeout liên tục 3 lần
        await using var db = await CreateSettlementDbAsync();
        var gateway = new MockNapasPayoutGateway();
        var escrowId = Guid.NewGuid();
        gateway.TimeoutNext(escrowId, 3);

        var service = CreateSettlementService(db, gateway, firstWait: TimeSpan.Zero);
        var command = new PayoutCommand
        {
            EscrowId = escrowId,
            SellerId = Guid.NewGuid(),
            Amount = 1_000_000m,
            RetryCount = 0,
            IdempotencyKey = PayoutIdempotency.Key(escrowId, 0),
            BankCode = "MB",
            AccountNumber = "0938434102",
            AccountName = "SELLER BAN"
        };

        // Act - Chạy 3 lần retry
        await service.AcceptAsync(command); // Attempt 1
        await service.RetryDueAsync(DateTimeOffset.UtcNow.AddMinutes(1)); // Attempt 2
        await service.RetryDueAsync(DateTimeOffset.UtcNow.AddMinutes(2)); // Attempt 3

        // Assert - Chuyển sang Exhausted khi vượt quá MaxAttempts = 3
        var order = await db.TransferOrders.SingleAsync(o => o.IdempotencyKey == command.IdempotencyKey);
        Assert.Equal(TransferState.Exhausted, order.State);
        Assert.Equal(SettlementPayoutService.MaxAttempts, order.AttemptCount);
        Assert.Null(order.BankReference);

        // Gọi lại không thực thi thêm gateway call
        var afterExhausted = await service.AcceptAsync(command);
        Assert.Equal(TransferState.Exhausted, afterExhausted.State);
        Assert.False(afterExhausted.GatewayCalled);
    }

    [Theory]
    [InlineData(1, 1)] // Attempt 1: 1 * 2^0 = 1s
    [InlineData(2, 2)] // Attempt 2: 1 * 2^1 = 2s
    [InlineData(3, 4)] // Attempt 3: 1 * 2^2 = 4s
    public void PayoutIdempotency_ExponentialBackoffSchedule_VerifiesIntervalCalculation(
        int completedAttempts, int expectedMultiplierSeconds)
    {
        // Arrange
        var baseInterval = TimeSpan.FromSeconds(1);

        // Act
        var delay = SettlementPayoutService.WaitAfterAttempt(completedAttempts, baseInterval);

        // Assert - Kiểm tra giãn cách tăng cấp số nhân (Exponential Backoff)
        Assert.Equal(TimeSpan.FromSeconds(expectedMultiplierSeconds), delay);
    }

    [Fact]
    public async Task PayoutIdempotency_CoreAtomicCas_PreventsConcurrentDoublePayoutRelease()
    {
        // Arrange - Kiểm thử CAS nguyên tử tại tầng Core DbContext (EscrowSettlementCas)
        var (conn, db) = CreateSqliteCoreDb();
        await using (conn)
        await using (db)
        {
            var escrowId = await SeedCoreEscrowAsync(db, 1_000_000m, 30_000m, EscrowStatus.Locked);
            var cas = new EscrowSettlementCas(db);

            // Act 1: Lệnh giải ngân đầu tiên
            var firstWin = await cas.TryBeginReleaseAsync(escrowId);

            // Act 2: Lệnh giải ngân thứ 2 chạy đồng thời / kế tiếp khi trạng thái đã sang Releasing
            var secondWin = await cas.TryBeginReleaseAsync(escrowId);

            // Assert - CAS chỉ cho phép ĐÚNG 1 lệnh thắng, lệnh sau bị từ chối
            Assert.True(firstWin);
            Assert.False(secondWin);

            // Kiểm tra trạng thái Escrow
            db.ChangeTracker.Clear();
            var updatedEscrow = await db.EscrowTransactions.SingleAsync(e => e.Id == escrowId);
            Assert.Equal(EscrowStatus.Releasing, updatedEscrow.Status);

            // OutboxMessage sinh ra DUY NHẤT 1 bản tin PayoutRequestedEvent
            var outboxMessages = await db.OutboxMessages.ToListAsync();
            Assert.Single(outboxMessages);
            Assert.Equal(nameof(PayoutRequestedEvent), outboxMessages[0].EventType);

            var payload = JsonSerializer.Deserialize<PayoutRequestedEvent>(outboxMessages[0].Payload);
            Assert.NotNull(payload);
            Assert.Equal(970_000m, payload.Amount); // Đúng NetSellerPayout (1.000.000 - 30.000)
            Assert.Equal($"IDEMP-{escrowId}-0", payload.IdempotencyKey);

            // PayoutTransaction tạo ra DUY NHẤT 1 bản ghi
            var payoutRecords = await db.PayoutTransactions.Where(p => p.EscrowId == escrowId).ToListAsync();
            Assert.Single(payoutRecords);
            Assert.Equal(PayoutStatus.Processing, payoutRecords[0].Status);
            Assert.Equal(970_000m, payoutRecords[0].Amount);
        }
    }

    #endregion
}
