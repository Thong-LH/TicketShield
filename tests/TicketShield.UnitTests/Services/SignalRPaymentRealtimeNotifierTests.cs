using Microsoft.AspNetCore.SignalR;
using Moq;
using TicketShield.API.Hubs;
using TicketShield.API.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

public class SignalRPaymentRealtimeNotifierTests
{
    private readonly Mock<IHubContext<PaymentHub>> _mockHubContext;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockListingGroupProxy;
    private readonly Mock<IClientProxy> _mockPaymentGroupProxy;
    private readonly SignalRPaymentRealtimeNotifier _sut;

    public SignalRPaymentRealtimeNotifierTests()
    {
        _mockHubContext = new Mock<IHubContext<PaymentHub>>();
        _mockClients = new Mock<IHubClients>();
        _mockListingGroupProxy = new Mock<IClientProxy>();
        _mockPaymentGroupProxy = new Mock<IClientProxy>();

        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);

        _mockClients.Setup(c => c.Group(It.Is<string>(s => s.StartsWith("listing_"))))
            .Returns(_mockListingGroupProxy.Object);
        _mockClients.Setup(c => c.Group(It.Is<string>(s => s.StartsWith("payment_"))))
            .Returns(_mockPaymentGroupProxy.Object);

        _sut = new SignalRPaymentRealtimeNotifier(_mockHubContext.Object);
    }

    [Fact]
    public async Task NotifyPaymentApprovedAsync_WithPaymentReference_SendsToBothListingAndPaymentGroups()
    {
        // Arrange
        var listingId = Guid.NewGuid();
        var payload = new
        {
            ListingId = listingId,
            PaymentReference = "TSREF-SEPAY-001",
            EscrowStatus = "Locked"
        };

        // Act
        await _sut.NotifyPaymentApprovedAsync(listingId, payload);

        // Assert
        var expectedListingGroup = $"listing_{listingId.ToString().ToLowerInvariant()}";
        var expectedPaymentGroup = "payment_TSREF-SEPAY-001";

        _mockClients.Verify(c => c.Group(expectedListingGroup), Times.Once);
        _mockClients.Verify(c => c.Group(expectedPaymentGroup), Times.Once);

        _mockListingGroupProxy.Verify(p => p.SendCoreAsync(
            "PaymentApproved",
            It.Is<object?[]>(args => args.Length == 1 && args[0] == payload),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockPaymentGroupProxy.Verify(p => p.SendCoreAsync(
            "PaymentApproved",
            It.Is<object?[]>(args => args.Length == 1 && args[0] == payload),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyPaymentApprovedAsync_WithoutPaymentReference_SendsOnlyToListingGroup()
    {
        // Arrange
        var listingId = Guid.NewGuid();
        var payload = new
        {
            ListingId = listingId,
            EscrowStatus = "Locked"
        };

        // Act
        await _sut.NotifyPaymentApprovedAsync(listingId, payload);

        // Assert
        var expectedListingGroup = $"listing_{listingId.ToString().ToLowerInvariant()}";
        _mockClients.Verify(c => c.Group(expectedListingGroup), Times.Once);
        _mockClients.Verify(c => c.Group(It.Is<string>(s => s.StartsWith("payment_"))), Times.Never);
    }

    [Fact]
    public async Task NotifyOrderSettledAsync_SendsToBothGroups()
    {
        // Arrange
        var listingId = Guid.NewGuid();
        var payload = new
        {
            listingId = listingId,
            paymentReference = "tsref-order-settled",
            escrowStatus = "Released"
        };

        // Act
        await _sut.NotifyOrderSettledAsync(listingId, payload);

        // Assert
        _mockClients.Verify(c => c.Group($"listing_{listingId.ToString().ToLowerInvariant()}"), Times.Once);
        _mockClients.Verify(c => c.Group("payment_TSREF-ORDER-SETTLED"), Times.Once);

        _mockListingGroupProxy.Verify(p => p.SendCoreAsync(
            "OrderSettled",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockPaymentGroupProxy.Verify(p => p.SendCoreAsync(
            "OrderSettled",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyHoldExpiredAsync_SendsToBothGroups()
    {
        // Arrange
        var listingId = Guid.NewGuid();
        var payload = new
        {
            listingId = listingId,
            paymentReference = "TSREF-HOLD-EXP",
            listingStatus = "Expired"
        };

        // Act
        await _sut.NotifyHoldExpiredAsync(listingId, payload);

        // Assert
        _mockClients.Verify(c => c.Group($"listing_{listingId.ToString().ToLowerInvariant()}"), Times.Once);
        _mockClients.Verify(c => c.Group("payment_TSREF-HOLD-EXP"), Times.Once);

        _mockListingGroupProxy.Verify(p => p.SendCoreAsync(
            "HoldExpired",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockPaymentGroupProxy.Verify(p => p.SendCoreAsync(
            "HoldExpired",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
