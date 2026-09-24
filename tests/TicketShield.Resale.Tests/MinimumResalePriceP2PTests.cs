using System.Net;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// BE-CORE-4.2.5 — P2P: resale price below the minimum seller fee is rejected
/// at publish, and an existing cheap listing cannot be held.
/// </summary>
public sealed class MinimumResalePriceP2PTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    private const string Buyer = ResaleFixture.OtherSeller;

    [Fact]
    public async Task Price_below_minimum_seller_fee_is_rejected_and_cannot_be_held()
    {
        var rejected = await PublishAsync(4000);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.Status);
        Assert.Equal("PRICE_BELOW_MINIMUM_SELLER_FEE", rejected.Body.GetProperty("message").GetString());

        var published = await PublishAsync(5000);
        Assert.Equal(HttpStatusCode.OK, published.Status);
        var listingId = published.Body.GetProperty("data").GetProperty("listingId").GetGuid();

        await f.Sql(
            f.Database.CoreConnection,
            "UPDATE resale_listings SET resale_price = 4000 WHERE id = @id",
            ("id", listingId));

        var hold = await f.Post($"api/v1/resale-listings/{listingId}/hold", new { }, seller: Buyer);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, hold.Status);
        Assert.Equal("Verified", await ListingStatusAsync(listingId));
        Assert.Null(await EscrowStatusAsync(listingId));
    }

    private async Task<(HttpStatusCode Status, System.Text.Json.JsonElement Body)> PublishAsync(long resalePrice)
    {
        var ticket = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticket);
        var confirmed = await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
        return await f.Post("api/resale-listings", new { verificationId, resalePrice, isPrivate = false });
    }

    private async Task<string?> ListingStatusAsync(Guid listingId) =>
        (string?)await f.Sql(
            f.Database.CoreConnection,
            "SELECT listing_status FROM resale_listings WHERE id=@id",
            ("id", listingId));

    private async Task<string?> EscrowStatusAsync(Guid listingId) =>
        (string?)await f.Sql(
            f.Database.CoreConnection,
            """
            SELECT status FROM escrow_transactions
            WHERE listing_id = @id
            ORDER BY created_at DESC
            LIMIT 1
            """,
            ("id", listingId));
}
