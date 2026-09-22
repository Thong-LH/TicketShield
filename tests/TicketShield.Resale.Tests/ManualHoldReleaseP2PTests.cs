using System.Net;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// BE-CORE-4.2.4 — P2P: buyer cancelling a hold must set escrow to Cancelled
/// (not leave it Pending) and return the listing to the marketplace.
/// </summary>
public sealed class ManualHoldReleaseP2PTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    private const string Buyer = ResaleFixture.OtherSeller;

    [Fact]
    public async Task Buyer_release_hold_cancels_escrow_and_returns_listing_to_marketplace()
    {
        var listingId = await PublishPublicListingAsync();

        var hold = await f.Post($"api/v1/resale-listings/{listingId}/hold", new { }, seller: Buyer);
        Assert.Equal(HttpStatusCode.OK, hold.Status);
        Assert.Equal("Transacting", hold.Body.GetProperty("data").GetProperty("listingStatus").GetString());
        Assert.Equal("Pending", await EscrowStatusAsync(listingId));

        var release = await f.Post($"api/v1/resale-listings/{listingId}/release-hold", new { }, seller: Buyer);
        Assert.Equal(HttpStatusCode.OK, release.Status);
        Assert.Equal("Verified", release.Body.GetProperty("data").GetProperty("listingStatus").GetString());

        Assert.Equal("Verified", await ListingStatusAsync(listingId));
        Assert.Equal("Cancelled", await EscrowStatusAsync(listingId));
        Assert.Contains(listingId.ToString(), await f.Http.GetStringAsync("api/resale-listings"));

        var holdAgain = await f.Post($"api/v1/resale-listings/{listingId}/hold", new { }, seller: Buyer);
        Assert.Equal(HttpStatusCode.OK, holdAgain.Status);
        Assert.Equal("Transacting", holdAgain.Body.GetProperty("data").GetProperty("listingStatus").GetString());
        Assert.Equal("Pending", await EscrowStatusAsync(listingId));
    }

    private async Task<Guid> PublishPublicListingAsync()
    {
        var ticket = await f.Ticket(price: 2_500_000m);
        var (verificationId, otp) = await f.Start(ticket);
        var confirmed = await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);

        var published = await f.Post("api/resale-listings", new { verificationId, resalePrice = 2_000_000, isPrivate = false });
        Assert.Equal(HttpStatusCode.OK, published.Status);
        return published.Body.GetProperty("data").GetProperty("listingId").GetGuid();
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
