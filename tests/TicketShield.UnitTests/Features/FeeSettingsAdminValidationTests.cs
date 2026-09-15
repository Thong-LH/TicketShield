using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.FeeSettings.Commands.UpdateFeeSettings;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using Xunit;

namespace TicketShield.UnitTests.Features;

public class FeeSettingsAdminValidationTests
{
    private readonly Mock<ISystemSettingRepository> _mockRepository;
    private readonly Mock<IResaleFeeCalculator> _mockFeeCalculator;
    private readonly Mock<ICurrentUserService> _mockCurrentUserService;
    private readonly UpdateFeeSettingsCommandValidator _validator;

    public FeeSettingsAdminValidationTests()
    {
        _mockRepository = new Mock<ISystemSettingRepository>();
        _mockFeeCalculator = new Mock<IResaleFeeCalculator>();
        _mockCurrentUserService = new Mock<ICurrentUserService>();
        _validator = new UpdateFeeSettingsCommandValidator();
    }

    [Fact]
    public async Task Admin_UpdateFeeConfig_ValidData_UpdatesDatabaseAndInvalidatesCache()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        _mockCurrentUserService.Setup(s => s.UserId).Returns(adminId);

        var command = new UpdateFeeSettingsCommand
        {
            BuyerFeePercentage = 0.04m,
            SellerFeePercentage = 0.02m,
            MinimumBuyerFee = 10000m,
            MinimumSellerFee = 5000m
        };

        var handler = new UpdateFeeSettingsCommandHandler(
            _mockRepository.Object,
            _mockFeeCalculator.Object,
            _mockCurrentUserService.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        _mockRepository.Verify(r => r.UpdateResaleFeeConfigAsync(
            It.Is<ResaleFeeConfig>(c =>
                c.BuyerFeePercentage == 0.04m &&
                c.SellerFeePercentage == 0.02m &&
                c.MinimumBuyerFee == 10000m &&
                c.MinimumSellerFee == 5000m),
            It.Is<Guid?>(id => id == adminId),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockFeeCalculator.Verify(f => f.InvalidateCache(), Times.Once);
    }

    [Theory]
    [InlineData(-0.01, 0.03, 10000, 5000)] // Negative buyer percentage
    [InlineData(0.16, 0.03, 10000, 5000)]  // Exceeds buyer max (15%)
    [InlineData(0.05, -0.01, 10000, 5000)] // Negative seller percentage
    [InlineData(0.05, 0.11, 10000, 5000)]  // Exceeds seller max (10%)
    [InlineData(0.05, 0.03, -1, 5000)]     // Negative minimum buyer fee
    [InlineData(0.05, 0.03, 10000, -1)]    // Negative minimum seller fee
    public void Admin_UpdateFeeConfig_OutOfBounds_ReturnsBadRequest(
        decimal buyerPct, decimal sellerPct, decimal minBuyer, decimal minSeller)
    {
        // Arrange
        var command = new UpdateFeeSettingsCommand
        {
            BuyerFeePercentage = buyerPct,
            SellerFeePercentage = sellerPct,
            MinimumBuyerFee = minBuyer,
            MinimumSellerFee = minSeller
        };

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void UnauthorizedUser_UpdateFeeConfig_Returns403Forbidden()
    {
        // Arrange - Verify controller attribute decoration
        var controllerType = typeof(API.Controllers.Admin.FeeSettingsController);
        var authorizeAttributes = controllerType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true);

        // Assert - Controller is strictly protected by Admin role
        Assert.NotEmpty(authorizeAttributes);
        var authAttr = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute)authorizeAttributes[0];
        Assert.Equal("Admin", authAttr.Roles);
    }
}
