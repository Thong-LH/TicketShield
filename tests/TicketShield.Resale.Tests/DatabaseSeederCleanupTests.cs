using Microsoft.Extensions.DependencyInjection;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// The Core seeder cleans old listings of the demo ticket codes. A listing whose escrow already
/// has money history (a payout or a dispute) must survive, otherwise the whole seed run fails
/// on the foreign key and the demo data is left half-updated.
/// </summary>
public sealed class DatabaseSeederCleanupTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    private const string TemplateListing = "cc111111-1111-1111-1111-111111111111";
    private const string TemplateEscrow = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeee01";

    [Fact]
    public async Task Reseeding_keeps_old_demo_listings_that_already_have_a_payout()
    {
        var listingId = Guid.NewGuid();
        var escrowId = Guid.NewGuid();
        var payoutId = Guid.NewGuid();
        await f.Sql(f.Database.CoreConnection,
            """
            INSERT INTO resale_listings
            SELECT (jsonb_populate_record(NULL::resale_listings, to_jsonb(l) ||
                jsonb_build_object('id', @listing, 'original_ticket_code', 'ATSH-GA-001',
                                   'listing_status', 'Sold', 'private_access_token', NULL))).*
            FROM resale_listings l WHERE l.id = @template
            """,
            ("listing", listingId), ("template", Guid.Parse(TemplateListing)));
        await f.Sql(f.Database.CoreConnection,
            """
            INSERT INTO escrow_transactions
            SELECT (jsonb_populate_record(NULL::escrow_transactions, to_jsonb(e) ||
                jsonb_build_object('id', @escrow, 'listing_id', @listing,
                                   'payment_reference', 'TSSEEDPAY01', 'bank_transaction_reference', 'FT_SEED_PAY_01'))).*
            FROM escrow_transactions e WHERE e.id = @template
            """,
            ("escrow", escrowId), ("listing", listingId), ("template", Guid.Parse(TemplateEscrow)));
        await f.Sql(f.Database.CoreConnection,
            """
            INSERT INTO payout_transactions (id, escrow_id, seller_id, payout_code, recipient_bank_code,
                recipient_account_number, recipient_account_name, amount, status, retry_count, created_at, updated_at)
            SELECT @payout, e.id, e.seller_id, 'PO-SEED-01', 'TEST', '123456789', 'Test Seller',
                e.net_seller_payout, 'Succeeded', 0, now(), now()
            FROM escrow_transactions e WHERE e.id = @escrow
            """,
            ("payout", payoutId), ("escrow", escrowId));

        using (var scope = f.Core.Services.CreateScope())
        {
            await DatabaseSeeder.SeedTicketShieldAsync(scope.ServiceProvider.GetRequiredService<TicketShieldDbContext>());
        }

        Assert.Equal(1L, await f.Sql(f.Database.CoreConnection,
            "SELECT count(*) FROM payout_transactions WHERE id = @id", ("id", payoutId)));
        Assert.Equal(1L, await f.Sql(f.Database.CoreConnection,
            "SELECT count(*) FROM resale_listings WHERE id = @id", ("id", listingId)));
    }

    [Fact]
    public async Task Reseeding_still_removes_old_demo_listings_without_money_history()
    {
        var listingId = Guid.NewGuid();
        await f.Sql(f.Database.CoreConnection,
            """
            INSERT INTO resale_listings
            SELECT (jsonb_populate_record(NULL::resale_listings, to_jsonb(l) ||
                jsonb_build_object('id', @listing, 'original_ticket_code', 'ATSH-GA-002',
                                   'listing_status', 'Cancelled', 'private_access_token', NULL))).*
            FROM resale_listings l WHERE l.id = @template
            """,
            ("listing", listingId), ("template", Guid.Parse(TemplateListing)));

        using (var scope = f.Core.Services.CreateScope())
        {
            await DatabaseSeeder.SeedTicketShieldAsync(scope.ServiceProvider.GetRequiredService<TicketShieldDbContext>());
        }

        Assert.Equal(0L, await f.Sql(f.Database.CoreConnection,
            "SELECT count(*) FROM resale_listings WHERE id = @id", ("id", listingId)));
    }
}
