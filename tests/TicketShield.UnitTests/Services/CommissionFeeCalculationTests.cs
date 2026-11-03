using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

public class CommissionFeeCalculationTests : IDisposable
{
    private readonly TicketShieldDbContext _dbContext;
    private readonly Mock<ISystemSettingRepository> _mockSettingRepo;
    private readonly IMemoryCache _memoryCache;

    public CommissionFeeCalculationTests()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TicketShieldDbContext(options);
        _mockSettingRepo = new Mock<ISystemSettingRepository>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        _mockSettingRepo.Setup(r => r.GetResaleFeeConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResaleFeeConfig
            {
                BuyerFeePercentage = 0.05m,
                SellerFeePercentage = 0.03m,
                MinimumBuyerFee = 10000m,
                MinimumSellerFee = 5000m
            });
    }

    [Fact]
    public async Task CalculateFee_WhenEventHasActiveCommission_UsesEventSpecificRates()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var commission = new Commission
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            BuyerFeeRate = 0.08m,      // 8% override
            SellerFeeRate = 0.04m,     // 4% override
            MinimumBuyerFee = 20000m,
            MinimumSellerFee = 10000m,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo = DateTimeOffset.UtcNow.AddDays(30),
            IsActive = true
        };
        _dbContext.Commissions.Add(commission);
        await _dbContext.SaveChangesAsync();

        var calculator = new DynamicResaleFeeCalculator(_mockSettingRepo.Object, _memoryCache, _dbContext);

        // Act
        var result = await calculator.CalculateFeeAsync(1_000_000m, isPrivate: false, eventId);

        // Assert
        Assert.Equal(80000m, result.BuyerFee);       // 1,000,000 * 8%
        Assert.Equal(40000m, result.SellerFee);      // 1,000,000 * 4%
        Assert.Equal(1_080000m, result.TotalBuyerPaid);
        Assert.Equal(960000m, result.NetSellerPayout);
    }

    [Fact]
    public async Task CalculateFee_WhenEventCommissionBelowMinimum_EnforcesEventMinimumFees()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var commission = new Commission
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            BuyerFeeRate = 0.08m,
            SellerFeeRate = 0.04m,
            MinimumBuyerFee = 25000m,
            MinimumSellerFee = 15000m,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo = DateTimeOffset.UtcNow.AddDays(30),
            IsActive = true
        };
        _dbContext.Commissions.Add(commission);
        await _dbContext.SaveChangesAsync();

        var calculator = new DynamicResaleFeeCalculator(_mockSettingRepo.Object, _memoryCache, _dbContext);

        // Act (50,000 * 8% = 4,000 < 25,000; 50,000 * 4% = 2,000 < 15,000)
        var result = await calculator.CalculateFeeAsync(50_000m, isPrivate: false, eventId);

        // Assert minimum fees applied
        Assert.Equal(25000m, result.BuyerFee);
        Assert.Equal(15000m, result.SellerFee);
        Assert.Equal(75000m, result.TotalBuyerPaid);
        Assert.Equal(35000m, result.NetSellerPayout);
    }

    [Fact]
    public async Task CalculateFee_WhenEventCommissionIsInactive_FallsBackToSystemSetting()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var commission = new Commission
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            BuyerFeeRate = 0.10m,
            SellerFeeRate = 0.05m,
            MinimumBuyerFee = 50000m,
            MinimumSellerFee = 30000m,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo = DateTimeOffset.UtcNow.AddDays(30),
            IsActive = false // INACTIVE
        };
        _dbContext.Commissions.Add(commission);
        await _dbContext.SaveChangesAsync();

        var calculator = new DynamicResaleFeeCalculator(_mockSettingRepo.Object, _memoryCache, _dbContext);

        // Act
        var result = await calculator.CalculateFeeAsync(1_000_000m, isPrivate: false, eventId);

        // Assert fallback to default system setting (5% buyer, 3% seller)
        Assert.Equal(50000m, result.BuyerFee);
        Assert.Equal(30000m, result.SellerFee);
        Assert.Equal(1_050000m, result.TotalBuyerPaid);
        Assert.Equal(970000m, result.NetSellerPayout);
    }

    [Fact]
    public async Task CalculateFee_WhenEventCommissionExpired_FallsBackToSystemSetting()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var commission = new Commission
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            BuyerFeeRate = 0.10m,
            SellerFeeRate = 0.05m,
            MinimumBuyerFee = 50000m,
            MinimumSellerFee = 30000m,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-10),
            EffectiveTo = DateTimeOffset.UtcNow.AddDays(-1), // EXPIRED
            IsActive = true
        };
        _dbContext.Commissions.Add(commission);
        await _dbContext.SaveChangesAsync();

        var calculator = new DynamicResaleFeeCalculator(_mockSettingRepo.Object, _memoryCache, _dbContext);

        // Act
        var result = await calculator.CalculateFeeAsync(1_000_000m, isPrivate: false, eventId);

        // Assert fallback to system setting
        Assert.Equal(50000m, result.BuyerFee);
        Assert.Equal(30000m, result.SellerFee);
    }

    [Fact]
    public async Task CalculateFee_WhenNoEventIdProvided_FallsBackToSystemSetting()
    {
        // Arrange
        var calculator = new DynamicResaleFeeCalculator(_mockSettingRepo.Object, _memoryCache, _dbContext);

        // Act
        var result = await calculator.CalculateFeeAsync(1_000_000m, isPrivate: false, eventId: null);

        // Assert
        Assert.Equal(50000m, result.BuyerFee);
        Assert.Equal(30000m, result.SellerFee);
    }

    [Fact]
    public async Task CalculateFee_WhenPriceBelowMinimumSellerFee_ThrowsBusinessRuleViolationException()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var commission = new Commission
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            BuyerFeeRate = 0.05m,
            SellerFeeRate = 0.03m,
            MinimumBuyerFee = 10000m,
            MinimumSellerFee = 15000m,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo = DateTimeOffset.UtcNow.AddDays(30),
            IsActive = true
        };
        _dbContext.Commissions.Add(commission);
        await _dbContext.SaveChangesAsync();

        var calculator = new DynamicResaleFeeCalculator(_mockSettingRepo.Object, _memoryCache, _dbContext);

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            calculator.CalculateFeeAsync(10_000m, isPrivate: false, eventId));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _memoryCache.Dispose();
    }
}
