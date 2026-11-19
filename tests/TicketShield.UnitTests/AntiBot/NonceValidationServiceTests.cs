using TicketShield.AntiBot.Gateway.Models;
using TicketShield.AntiBot.Gateway.Services;
using Xunit;

namespace TicketShield.UnitTests.AntiBot;

public class NonceValidationServiceTests
{
    private readonly NonceValidationService _service;

    public NonceValidationServiceTests()
    {
        // Khởi tạo service không có Redis để kiểm tra In-Memory Fallback
        _service = new NonceValidationService();
    }

    [Fact]
    public async Task ValidateAndConsume_FirstTime_ReturnsSuccess()
    {
        // Arrange
        var nonce = Guid.NewGuid().ToString("N");
        var tierId = Guid.NewGuid();
        await _service.RegisterNonceAsync(nonce, tierId, TimeSpan.FromMinutes(5));

        // Act
        var result = await _service.ValidateAndConsumeNonceAsync(nonce, tierId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsReplay);
        Assert.False(result.IsExpired);
    }

    [Fact]
    public async Task ValidateAndConsume_SameNonceTwice_SecondCallDetectsReplay()
    {
        // Arrange
        var nonce = Guid.NewGuid().ToString("N");
        var tierId = Guid.NewGuid();
        await _service.RegisterNonceAsync(nonce, tierId, TimeSpan.FromMinutes(5));

        // Act 1: Lần đầu tiên tiêu thụ nonce
        var firstResult = await _service.ValidateAndConsumeNonceAsync(nonce, tierId);
        Assert.True(firstResult.IsSuccess);

        // Act 2: Kẻ tấn công phát lại (Replay) cùng một nonce lần thứ hai
        var secondResult = await _service.ValidateAndConsumeNonceAsync(nonce, tierId);

        // Assert: Bắt quả tang Replay Attack!
        Assert.False(secondResult.IsSuccess);
        Assert.True(secondResult.IsReplay);
        Assert.False(secondResult.IsExpired);
    }

    [Fact]
    public async Task ValidateAndConsume_UnknownNonce_ReturnsExpired()
    {
        // Arrange: Nonce chưa từng được cấp phát hoặc đã bị xóa
        var unknownNonce = Guid.NewGuid().ToString("N");
        var tierId = Guid.NewGuid();

        // Act
        var result = await _service.ValidateAndConsumeNonceAsync(unknownNonce, tierId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.IsReplay);
        Assert.True(result.IsExpired);
    }

    [Fact]
    public async Task ValidateAndConsume_MismatchedTierId_ReturnsFailed()
    {
        // Arrange: Cấp cho Tier A nhưng gửi checkout cho Tier B
        var nonce = Guid.NewGuid().ToString("N");
        var issuedTierId = Guid.NewGuid();
        var checkoutTierId = Guid.NewGuid();
        await _service.RegisterNonceAsync(nonce, issuedTierId, TimeSpan.FromMinutes(5));

        // Act
        var result = await _service.ValidateAndConsumeNonceAsync(nonce, checkoutTierId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.IsReplay);
        Assert.False(result.IsExpired);
        Assert.Contains("different ticket tier", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAndConsume_ExpiredNonce_ReturnsExpired()
    {
        // Arrange: Cấp nonce với TTL cực ngắn (100ms)
        var nonce = Guid.NewGuid().ToString("N");
        var tierId = Guid.NewGuid();
        await _service.RegisterNonceAsync(nonce, tierId, TimeSpan.FromMilliseconds(50));

        // Đợi 120ms để nonce quá hạn
        await Task.Delay(120);

        // Act
        var result = await _service.ValidateAndConsumeNonceAsync(nonce, tierId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsExpired);
    }
}
