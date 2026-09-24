using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TicketShield.Infrastructure.Workers;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// BE-CORE-4.2.3 — P2P: when a buyer hold times out after the 2-hour resale cut-off,
/// the listing must expire instead of returning to the public marketplace.
/// </summary>
public sealed class ExpiredHoldCutoffP2PTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    private const string Buyer = ResaleFixture.OtherSeller;

    [Fact]
    public async Task Hold_timeout_after_resale_cutoff_expires_listing_and_hides_it_from_marketplace()
    {
        var listingId = await PublishPublicListingAsync();

        var marketBefore = await f.Http.GetStringAsync("api/resale-listings");
        Assert.Contains(listingId.ToString(), marketBefore);

        var hold = await f.Post($"api/v1/resale-listings/{listingId}/hold", new { }, seller: Buyer);
        Assert.Equal(HttpStatusCode.OK, hold.Status);
        Assert.Equal("Transacting", hold.Body.GetProperty("data").GetProperty("listingStatus").GetString());

        await MoveEventWithinCutoffAsync(listingId);
        await ExpireHoldWindowAsync(listingId);
        await RunHoldReleaseWorkerAsync();

        Assert.Equal("Expired", await ListingStatusAsync(listingId));
        Assert.Equal("Expired", await EscrowStatusAsync(listingId));
        Assert.DoesNotContain(listingId.ToString(), await f.Http.GetStringAsync("api/resale-listings"));

        var holdAgain = await f.Post($"api/v1/resale-listings/{listingId}/hold", new { }, seller: Buyer);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, holdAgain.Status);
        Assert.Contains("Expired", holdAgain.Body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Hold_timeout_while_event_is_still_open_returns_listing_to_marketplace()
    {
        var listingId = await PublishPublicListingAsync();

        var hold = await f.Post($"api/v1/resale-listings/{listingId}/hold", new { }, seller: Buyer);
        Assert.Equal(HttpStatusCode.OK, hold.Status);

        await MoveEventDaysFromNowAsync(listingId, 15);
        await ExpireHoldWindowAsync(listingId);
        await RunHoldReleaseWorkerAsync();

        Assert.Equal("Verified", await ListingStatusAsync(listingId));
        Assert.Contains(listingId.ToString(), await f.Http.GetStringAsync("api/resale-listings"));
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

    private Task MoveEventWithinCutoffAsync(Guid listingId) =>
        MoveEventDaysFromNowAsync(listingId, days: 0, extraHours: 1);

    private async Task MoveEventDaysFromNowAsync(Guid listingId, int days, int extraHours = 0)
    {
        var start = DateTimeOffset.UtcNow.AddDays(days).AddHours(extraHours);
        await f.Sql(
            f.Database.CoreConnection,
            """
            UPDATE events
            SET event_start_at = @start, event_end_at = @end
            WHERE id = (SELECT event_id FROM resale_listings WHERE id = @id)
            """,
            ("start", start),
            ("end", start.AddHours(3)),
            ("id", listingId));
    }

    private async Task ExpireHoldWindowAsync(Guid listingId) =>
        await f.Sql(
            f.Database.CoreConnection,
            """
            UPDATE escrow_transactions
            SET unlock_at = @unlock
            WHERE listing_id = @id AND status = 'Pending'
            """,
            ("unlock", DateTimeOffset.UtcNow.AddMinutes(-1)),
            ("id", listingId));

    private async Task RunHoldReleaseWorkerAsync()
    {
        var worker = new ExpiredHoldReleaseWorker(
            f.Core.Services.GetRequiredService<IServiceScopeFactory>(),
            f.Core.Services.GetRequiredService<ILogger<ExpiredHoldReleaseWorker>>());
        await worker.ReleaseExpiredHoldsAsync();
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
