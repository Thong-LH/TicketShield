using System.Text.Json;
using TicketShield.Application.Features.Admin.Listings.Models;
using TicketShield.Application.Features.Admin.Transactions.Models;
using TicketShield.Application.Features.Orders.Models;
using TicketShield.Domain.Enums;
using Xunit;

namespace TicketShield.UnitTests.Features;

/// <summary>
/// Core API không bật JsonStringEnumConverter toàn cục, nên các DTO Epic-11 phải tự
/// trả trạng thái dạng chữ như các API cũ (FE đọc "Locked", không đọc 1).
/// </summary>
public class Epic11DtoEnumJsonTests
{
    // Giống cấu hình mặc định của ASP.NET Core AddControllers().
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AdminTransactionDto_SerializesStatusesAsNames()
    {
        var json = JsonSerializer.Serialize(new AdminTransactionDto
        {
            Status = EscrowStatus.Locked,
            PayoutStatus = PayoutStatus.Processing,
            DisputeStatus = DisputeStatus.UnderReview
        }, WebJson);

        Assert.Contains("\"status\":\"Locked\"", json);
        Assert.Contains("\"payoutStatus\":\"Processing\"", json);
        Assert.Contains("\"disputeStatus\":\"UnderReview\"", json);
    }

    [Fact]
    public void AdminTransactionDto_KeepsMissingPayoutAndDisputeAsNull()
    {
        var json = JsonSerializer.Serialize(new AdminTransactionDto
        {
            Status = EscrowStatus.Pending,
            PayoutStatus = null,
            DisputeStatus = null
        }, WebJson);

        Assert.Contains("\"status\":\"Pending\"", json);
        Assert.Contains("\"payoutStatus\":null", json);
        Assert.Contains("\"disputeStatus\":null", json);
    }

    [Fact]
    public void AdminListingDto_SerializesStatusesAsNames()
    {
        var json = JsonSerializer.Serialize(new AdminListingDto
        {
            ListingStatus = ListingStatus.Sold,
            VerificationStatus = VerificationStatus.Rejected
        }, WebJson);

        Assert.Contains("\"listingStatus\":\"Sold\"", json);
        Assert.Contains("\"verificationStatus\":\"Rejected\"", json);
    }

    [Fact]
    public void BuyerOrderDto_SerializesStatusesAsNames()
    {
        var json = JsonSerializer.Serialize(new BuyerOrderDto
        {
            Status = EscrowStatus.RefundQueued,
            DisputeStatus = DisputeStatus.Resolved
        }, WebJson);

        Assert.Contains("\"status\":\"RefundQueued\"", json);
        Assert.Contains("\"disputeStatus\":\"Resolved\"", json);
    }

    [Fact]
    public void BuyerOrderDto_KeepsMissingDisputeAsNull()
    {
        var json = JsonSerializer.Serialize(new BuyerOrderDto
        {
            Status = EscrowStatus.Released,
            DisputeStatus = null
        }, WebJson);

        Assert.Contains("\"status\":\"Released\"", json);
        Assert.Contains("\"disputeStatus\":null", json);
    }
}
