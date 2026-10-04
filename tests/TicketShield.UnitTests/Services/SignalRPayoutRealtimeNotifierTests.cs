using Microsoft.AspNetCore.SignalR;
using Moq;
using TicketShield.API.Hubs;
using TicketShield.API.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

public class SignalRPayoutRealtimeNotifierTests
{
    private readonly Mock<IHubContext<PaymentHub>> _mockHubContext;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockSellerGroupProxy;
    private readonly Mock<IClientProxy> _mockUserGroupProxy;
    private readonly Mock<IClientProxy> _mockListingGroupProxy;
    private readonly Mock<IClientProxy> _mockUserProxy;
    private readonly SignalRPayoutRealtimeNotifier _sut;

    public SignalRPayoutRealtimeNotifierTests()
    {
        _mockHubContext = new Mock<IHubContext<PaymentHub>>();
        _mockClients = new Mock<IHubClients>();
        _mockSellerGroupProxy = new Mock<IClientProxy>();
        _mockUserGroupProxy = new Mock<IClientProxy>();
        _mockListingGroupProxy = new Mock<IClientProxy>();
        _mockUserProxy = new Mock<IClientProxy>();

        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);

        _mockClients.Setup(c => c.Group(It.Is<string>(s => s.StartsWith("seller_"))))
            .Returns(_mockSellerGroupProxy.Object);
        _mockClients.Setup(c => c.Group(It.Is<string>(s => s.StartsWith("user_"))))
            .Returns(_mockUserGroupProxy.Object);
        _mockClients.Setup(c => c.Group(It.Is<string>(s => s.StartsWith("listing_"))))
            .Returns(_mockListingGroupProxy.Object);
        _mockClients.Setup(c => c.User(It.IsAny<string>()))
            .Returns(_mockUserProxy.Object);

        _sut = new SignalRPayoutRealtimeNotifier(_mockHubContext.Object);
    }

    [Fact]
    public async Task NotifyPayoutCompletedAsync_SendsPayoutCompletedEventToSellerAndListingGroups()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var escrowId = Guid.NewGuid();
        var listingId = Guid.NewGuid();
        var payload = new
        {
            EscrowId = escrowId,
            ListingId = listingId,
            SellerId = sellerId,
            Amount = 2425000m,
            BankCode = "MB",
            AccountNumber = "0329952127",
            Status = "Success"
        };

        // Act
        await _sut.NotifyPayoutCompletedAsync(sellerId, escrowId, listingId, payload);

        // Assert
        var expectedSellerGroup = $"seller_{sellerId.ToString().ToLowerInvariant()}";
        var expectedUserGroup = $"user_{sellerId.ToString().ToLowerInvariant()}";
        var expectedListingGroup = $"listing_{listingId.ToString().ToLowerInvariant()}";

        _mockClients.Verify(c => c.Group(expectedSellerGroup), Times.Once);
        _mockClients.Verify(c => c.Group(expectedUserGroup), Times.Once);
        _mockClients.Verify(c => c.Group(expectedListingGroup), Times.Once);

        _mockSellerGroupProxy.Verify(p => p.SendCoreAsync(
            "PayoutCompleted",
            It.Is<object?[]>(args => args.Length == 1 && args[0] == payload),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
