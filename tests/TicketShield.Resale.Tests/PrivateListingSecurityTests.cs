using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// SCRUM-38 · TEST-2.5.3 — Security tests for private resale listings and their share token.
///
/// Listings are created through the real seller flow (request OTP → confirm → publish), then the
/// REST endpoints used by the web app are called over HTTP exactly like a browser would:
///   GET  /api/v1/resale-listings/private/{shareToken}   (anonymous — buyer opens the shared link)
///   GET  /api/v1/resale-listings/{id}?token=...         (anonymous — listing detail)
///   GET  /api/v1/resale-listings/my-listings            (JWT)
///   POST /api/v1/resale-listings/{id}/cancel            (JWT)
///
/// Rules agreed with BE (Thịnh, 11/09/2026):
///   • unknown token → 404; the ticket code is always masked before escrow payment
///   • a cancelled private listing still opens (200) and shows ListingStatus = "Cancelled"
///   • cancelling must unlock the original ticket at the organizer, otherwise nothing is cancelled
/// </summary>
public sealed class PrivateListingSecurityTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    private sealed record PublishedListing(Guid Id, string? ShareToken, string TicketCode);

    /// Runs the real seller flow and returns the new listing.
    private async Task<PublishedListing> PublishListingAsync(bool isPrivate, long resalePrice = 2_000_000)
    {
        var ticket = await f.Ticket(price: 2_500_000m);
        var (verificationId, otp) = await f.Start(ticket);

        var confirmed = await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);

        var published = await f.Post("api/resale-listings", new { verificationId, resalePrice, isPrivate });
        Assert.Equal(HttpStatusCode.OK, published.Status);

        var data = published.Body.GetProperty("data");
        var token = data.TryGetProperty("privateAccessToken", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        return new PublishedListing(data.GetProperty("listingId").GetGuid(), token, ticket);
    }

    /// Sends a request with or without a login token. Returns the raw body so we can also check what is NOT in it.
    private async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpMethod method, string path, string? seller = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post) request.Content = JsonContent.Create(new { });
        if (seller is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", f.Token(seller));
        using var response = await f.Http.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static JsonElement Data(string body)
    {
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("data").Clone();
    }

    private async Task<string?> ListingStatusInDatabase(Guid listingId) =>
        (string?)await f.Sql(f.Database.CoreConnection, "SELECT listing_status FROM resale_listings WHERE id=@id", ("id", listingId));

    private async Task<string?> OrganizerTicketStatus(string ticketCode) =>
        (string?)await f.Sql(f.Database.MockConnection, "SELECT status FROM mock_tickets WHERE ticket_code=@code", ("code", ticketCode));

    // ───────────────────────── Share token: who can open the private link ─────────────────────────

    [Fact]
    public async Task Private_listing_opens_with_its_share_token_without_login_and_the_ticket_code_is_masked()
    {
        var listing = await PublishListingAsync(isPrivate: true);

        var (status, body) = await SendAsync(HttpMethod.Get, $"api/v1/resale-listings/private/{listing.ShareToken}");

        Assert.Equal(HttpStatusCode.OK, status);
        var data = Data(body);
        Assert.Equal(listing.Id, data.GetProperty("listingId").GetGuid());
        Assert.True(data.GetProperty("isPrivate").GetBoolean());
        Assert.Contains("*", data.GetProperty("maskedTicketCode").GetString());
        Assert.DoesNotContain(listing.TicketCode, body); // the real ticket code must never leave the server here
    }

    [Theory]
    [InlineData("not-a-real-share-token")]
    [InlineData("00000000000000000000000000000000")]
    public async Task Private_route_returns_404_for_unknown_share_tokens(string token)
    {
        await PublishListingAsync(isPrivate: true);

        var (status, _) = await SendAsync(HttpMethod.Get, $"api/v1/resale-listings/private/{Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Private_route_rejects_a_blank_share_token_without_revealing_a_listing()
    {
        var listing = await PublishListingAsync(isPrivate: true);

        var (status, body) = await SendAsync(HttpMethod.Get, "api/v1/resale-listings/private/%20");

        // A blank token is answered with 400, not 404: [ApiController] runs its implicit required
        // check on the route value before the request reaches the handler, and RequiredAttribute
        // trims strings, so " " is rejected as a missing parameter. The handler itself would answer
        // NotFound (see GetResaleListingByPrivateTokenQueryHandlerTests). Both answers are safe -
        // what SCRUM-38 requires is that a blank token never returns a listing.
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.DoesNotContain(listing.ShareToken, body);
        Assert.DoesNotContain(listing.TicketCode, body);
    }

    [Fact]
    public async Task Share_token_that_differs_by_one_character_is_rejected()
    {
        var listing = await PublishListingAsync(isPrivate: true);
        var token = listing.ShareToken!;
        var guessed = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');

        var (status, body) = await SendAsync(HttpMethod.Get, $"api/v1/resale-listings/private/{guessed}");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.DoesNotContain(listing.Id.ToString(), body);
    }

    [Fact]
    public async Task Public_listing_cannot_be_opened_through_the_private_route()
    {
        var listing = await PublishListingAsync(isPrivate: false);

        Assert.Null(listing.ShareToken);
        var (status, _) = await SendAsync(HttpMethod.Get, $"api/v1/resale-listings/private/{listing.Id}");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Share_tokens_are_128_bit_random_hex_and_unique_per_listing()
    {
        var first = await PublishListingAsync(isPrivate: true);
        var second = await PublishListingAsync(isPrivate: true);

        Assert.Matches(new Regex("^[0-9a-f]{32}$"), first.ShareToken!);
        Assert.Matches(new Regex("^[0-9a-f]{32}$"), second.ShareToken!);
        Assert.NotEqual(first.ShareToken, second.ShareToken);
    }

    [Fact]
    public async Task Private_listing_detail_by_id_requires_the_matching_token()
    {
        var listing = await PublishListingAsync(isPrivate: true);
        var path = $"api/v1/resale-listings/{listing.Id}";

        var noToken = await SendAsync(HttpMethod.Get, path);
        var wrongToken = await SendAsync(HttpMethod.Get, $"{path}?token=wrong-token");
        var rightToken = await SendAsync(HttpMethod.Get, $"{path}?token={listing.ShareToken}");

        Assert.Equal(HttpStatusCode.Forbidden, noToken.Status);
        Assert.Equal(HttpStatusCode.Forbidden, wrongToken.Status);
        Assert.Equal(HttpStatusCode.OK, rightToken.Status);
        Assert.DoesNotContain(listing.TicketCode, noToken.Body);
        Assert.DoesNotContain(listing.TicketCode, wrongToken.Body);
    }

    // ───────────────────────── My listings: tokens never leak to other users ─────────────────────────

    [Fact]
    public async Task My_listings_requires_login()
    {
        var (status, _) = await SendAsync(HttpMethod.Get, "api/v1/resale-listings/my-listings");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task My_listings_shows_the_share_token_to_its_seller_but_never_to_another_user()
    {
        var listing = await PublishListingAsync(isPrivate: true);

        var owner = await SendAsync(HttpMethod.Get, "api/v1/resale-listings/my-listings", ResaleFixture.Seller);
        var stranger = await SendAsync(HttpMethod.Get, "api/v1/resale-listings/my-listings", ResaleFixture.OtherSeller);

        Assert.Equal(HttpStatusCode.OK, owner.Status);
        Assert.Contains(listing.ShareToken!, owner.Body);
        Assert.Equal(HttpStatusCode.OK, stranger.Status);
        Assert.DoesNotContain(listing.Id.ToString(), stranger.Body);
        Assert.DoesNotContain(listing.ShareToken!, stranger.Body);
    }

    // ───────────────────────── Cancel: only the seller, and never half-way ─────────────────────────

    [Fact]
    public async Task Cancel_requires_login_and_changes_nothing()
    {
        var listing = await PublishListingAsync(isPrivate: true);

        var (status, _) = await SendAsync(HttpMethod.Post, $"api/v1/resale-listings/{listing.Id}/cancel");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("Verified", await ListingStatusInDatabase(listing.Id));
        Assert.Equal("LOCKED_FOR_RESALE", await OrganizerTicketStatus(listing.TicketCode));
    }

    [Fact]
    public async Task Another_seller_cannot_cancel_someone_elses_listing()
    {
        var listing = await PublishListingAsync(isPrivate: true);

        var (status, _) = await SendAsync(HttpMethod.Post, $"api/v1/resale-listings/{listing.Id}/cancel", ResaleFixture.OtherSeller);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("Verified", await ListingStatusInDatabase(listing.Id));
        Assert.Equal("LOCKED_FOR_RESALE", await OrganizerTicketStatus(listing.TicketCode));
    }

    [Fact]
    public async Task Seller_cancel_unlocks_the_ticket_and_the_old_private_link_then_shows_cancelled()
    {
        var listing = await PublishListingAsync(isPrivate: true);

        var cancel = await SendAsync(HttpMethod.Post, $"api/v1/resale-listings/{listing.Id}/cancel", ResaleFixture.Seller);

        Assert.Equal(HttpStatusCode.OK, cancel.Status);
        Assert.Equal("Cancelled", await ListingStatusInDatabase(listing.Id));
        Assert.Equal("VALID", await OrganizerTicketStatus(listing.TicketCode)); // ticket handed back to its owner

        var (linkStatus, linkBody) = await SendAsync(HttpMethod.Get, $"api/v1/resale-listings/private/{listing.ShareToken}");
        Assert.Equal(HttpStatusCode.OK, linkStatus);
        Assert.Equal("Cancelled", Data(linkBody).GetProperty("listingStatus").GetString());
    }

    [Fact(Skip = "Purchase/escrow API does not exist yet. Agreed with BE: buying a Cancelled listing must return 400. Write this test when the purchase endpoint is added.")]
    public void Buying_a_cancelled_private_listing_is_rejected_with_400()
    {
    }
}
