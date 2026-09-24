using Microsoft.Extensions.Caching.Memory;
using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

public class DynamicResaleFeeCalculatorTests
{
    private readonly Mock<ISystemSettingRepository> _mockRepository;
    private readonly IMemoryCache _memoryCache;

    public DynamicResaleFeeCalculatorTests()
    {
        _mockRepository = new Mock<ISystemSettingRepository>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
    }

    [Theory]
    [InlineData(1000000, 0.05, 0.03, 10000, 5000)]
    [InlineData(50000, 0.05, 0.03, 10000, 5000)] // Tests minimum fee threshold
    [InlineData(2500000, 0.04, 0.02, 10000, 5000)]
    public async Task UniformFee_PrivateAndPublic_CalculatesIdenticalAmount(
        decimal resalePrice, decimal buyerPct, decimal sellerPct, decimal minBuyer, decimal minSeller)
    {
        // Arrange
        var feeConfig = new ResaleFeeConfig
        {
            BuyerFeePercentage = buyerPct,
            SellerFeePercentage = sellerPct,
            MinimumBuyerFee = minBuyer,
            MinimumSellerFee = minSeller
        };

        _mockRepository.Setup(r => r.GetResaleFeeConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(feeConfig);

        var calculator = new DynamicResaleFeeCalculator(_mockRepository.Object, _memoryCache);

        // Act - Calculate fee for Public Resale and Private Resale
        var publicResult = await calculator.CalculateFeeAsync(resalePrice, isPrivate: false);
        var privateResult = await calculator.CalculateFeeAsync(resalePrice, isPrivate: true);

        // Assert - Fees must be 100% identical regardless of public vs private resale channel
        Assert.Equal(publicResult.BuyerFee, privateResult.BuyerFee);
        Assert.Equal(publicResult.SellerFee, privateResult.SellerFee);
        Assert.Equal(publicResult.TotalBuyerPaid, privateResult.TotalBuyerPaid);
        Assert.Equal(publicResult.NetSellerPayout, privateResult.NetSellerPayout);

        // Validate fee calculations match business formula
        decimal expectedBuyerFee = Math.Round(Math.Max(resalePrice * buyerPct, minBuyer), 0);
        decimal expectedSellerFee = Math.Round(Math.Max(resalePrice * sellerPct, minSeller), 0);

        Assert.Equal(expectedBuyerFee, publicResult.BuyerFee);
        Assert.Equal(expectedSellerFee, publicResult.SellerFee);
        Assert.Equal(resalePrice + expectedBuyerFee, publicResult.TotalBuyerPaid);
        Assert.Equal(resalePrice - expectedSellerFee, publicResult.NetSellerPayout);
    }

    [Fact]
    public async Task FeeSnapshot_PreservesOriginalAmountOnAdminUpdate()
    {
        // Arrange - Step 1: Initialize initial fee config (Buyer 5%, Seller 3%)
        var initialFeeConfig = new ResaleFeeConfig
        {
            BuyerFeePercentage = 0.05m,
            SellerFeePercentage = 0.03m,
            MinimumBuyerFee = 10000m,
            MinimumSellerFee = 5000m
        };

        _mockRepository.Setup(r => r.GetResaleFeeConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialFeeConfig);

        var calculator = new DynamicResaleFeeCalculator(_mockRepository.Object, _memoryCache);
        decimal resalePrice = 1000000m;

        // Step 2: Buyer checkout -> snapshot fees into EscrowTransaction
        var initialCalc = await calculator.CalculateFeeAsync(resalePrice, isPrivate: false);
        var escrow = new EscrowTransaction
        {
            ListingId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            OriginalTicketPrice = resalePrice,
            BuyerFee = initialCalc.BuyerFee,
            SellerFee = initialCalc.SellerFee,
            TotalBuyerPaid = initialCalc.TotalBuyerPaid,
            NetSellerPayout = initialCalc.NetSellerPayout,
            Status = EscrowStatus.Pending
        };

        // Assert initial snapshot amounts
        Assert.Equal(50000m, escrow.BuyerFee);
        Assert.Equal(30000m, escrow.SellerFee);
        Assert.Equal(1050000m, escrow.TotalBuyerPaid);
        Assert.Equal(970000m, escrow.NetSellerPayout);

        // Step 3: Admin updates fee config to 10% Buyer, 5% Seller and invalidates cache
        var updatedFeeConfig = new ResaleFeeConfig
        {
            BuyerFeePercentage = 0.10m,
            SellerFeePercentage = 0.05m,
            MinimumBuyerFee = 10000m,
            MinimumSellerFee = 5000m
        };

        _mockRepository.Setup(r => r.GetResaleFeeConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedFeeConfig);
        calculator.InvalidateCache();

        // Calculate fee for NEW transaction under updated rate
        var newCalc = await calculator.CalculateFeeAsync(resalePrice, isPrivate: false);

        // Assert — Existing locked Escrow transaction preserves original snapshot values 100%
        Assert.Equal(50000m, escrow.BuyerFee);
        Assert.Equal(30000m, escrow.SellerFee);
        Assert.Equal(1050000m, escrow.TotalBuyerPaid);
        Assert.Equal(970000m, escrow.NetSellerPayout);

        // Assert — New transaction uses updated 10%/5% rates
        Assert.Equal(100000m, newCalc.BuyerFee);
        Assert.Equal(50000m, newCalc.SellerFee);
    }

    [Fact]
    public async Task CalculateFee_WhenResalePriceBelowMinimumSellerFee_Throws()
    {
        var feeConfig = new ResaleFeeConfig
        {
            BuyerFeePercentage = 0.05m,
            SellerFeePercentage = 0.03m,
            MinimumBuyerFee = 10000m,
            MinimumSellerFee = 5000m
        };

        _mockRepository.Setup(r => r.GetResaleFeeConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(feeConfig);

        var calculator = new DynamicResaleFeeCalculator(_mockRepository.Object, _memoryCache);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            calculator.CalculateFeeAsync(4000m, isPrivate: false));
    }

    [Fact]
    public async Task CalculateFee_WhenResalePriceEqualsMinimumSellerFee_ReturnsZeroPayout()
    {
        var feeConfig = new ResaleFeeConfig
        {
            BuyerFeePercentage = 0.05m,
            SellerFeePercentage = 0.03m,
            MinimumBuyerFee = 10000m,
            MinimumSellerFee = 5000m
        };

        _mockRepository.Setup(r => r.GetResaleFeeConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(feeConfig);

        var calculator = new DynamicResaleFeeCalculator(_mockRepository.Object, _memoryCache);

        var result = await calculator.CalculateFeeAsync(5000m, isPrivate: false);

        Assert.Equal(5000m, result.SellerFee);
        Assert.Equal(0m, result.NetSellerPayout);
    }
}
