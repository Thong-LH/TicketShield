using Microsoft.Extensions.DependencyInjection;
using MockOrganizer.API.Data;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// Startup data mode "Keep" (Startup:DataMode=Keep, the default): restarting MockOrganizer or Core
/// must not delete or overwrite existing data. Only the demo event dates move, so the demo still
/// works on the day it is shown. "Reset" is covered by DatabaseSeederCleanupTests,
/// PreserveTransferredBuyerTicketsP2PTests (reset-all) and the GateScanServiceTests unit tests.
/// </summary>
public sealed class StartupKeepModeTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    private static readonly Guid GaStandingTier = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid GateDemoSeedTicket = Guid.Parse("a0000000-0000-0000-0000-000000000001"); // ATSH-VIP-888
    private static readonly Guid MarketplaceEvent = Guid.Parse("e1111111-1111-1111-1111-111111111111");
    private static readonly Guid DemoModerator = Guid.Parse("33333333-3333-3333-3333-333333333333"); // not used by the fixture flows
    private static readonly Guid TemplateListing = Guid.Parse("cc111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Mock_restart_in_keep_mode_keeps_tickets_gate_history_otps_locks_and_stock_but_moves_the_demo_date()
    {
        var extraCode = await f.Ticket();
        await f.Start(extraCode); // leaves an OTP and resale session records behind
        await f.Sql(f.Database.MockConnection,
            "INSERT INTO gate_access_logs (id, ticket_code, gate_name, scanned_at, scan_result) VALUES (@id, @code, 'Gate A', now(), 'SUCCESS')",
            ("id", Guid.NewGuid()), ("code", extraCode));
        await f.Sql(f.Database.MockConnection,
            "UPDATE mock_tickets SET status = 'USED', event_start_at = now() - interval '30 days' WHERE id = @id",
            ("id", GateDemoSeedTicket));
        await f.Sql(f.Database.MockConnection,
            "UPDATE ticket_tiers SET available_quantity = 5 WHERE id = @id", ("id", GaStandingTier));
        var resaleRecords = (long)(await f.Sql(f.Database.MockConnection, "SELECT count(*) FROM organizer_resale_records"))!;
        var otps = (long)(await f.Sql(f.Database.MockConnection, "SELECT count(*) FROM mock_otps"))!;
        Assert.True(resaleRecords > 0 && otps > 0);

        using (var scope = f.Mock.Services.CreateScope())
        {
            await OrganizerDatabaseSeeder.SeedOrganizerAsync(
                scope.ServiceProvider.GetRequiredService<OrganizerDbContext>(), keepExistingData: true);
        }

        Assert.Equal(1L, await f.Sql(f.Database.MockConnection,
            "SELECT count(*) FROM mock_tickets WHERE ticket_code = @code", ("code", extraCode)));
        Assert.Equal(1L, await f.Sql(f.Database.MockConnection,
            "SELECT count(*) FROM gate_access_logs WHERE ticket_code = @code", ("code", extraCode)));
        Assert.Equal(resaleRecords, await f.Sql(f.Database.MockConnection, "SELECT count(*) FROM organizer_resale_records"));
        Assert.Equal(otps, await f.Sql(f.Database.MockConnection, "SELECT count(*) FROM mock_otps"));
        Assert.Equal(5, await f.Sql(f.Database.MockConnection,
            "SELECT available_quantity FROM ticket_tiers WHERE id = @id", ("id", GaStandingTier)));

        // The demo ticket keeps its state but its event date moves back to the demo slot.
        Assert.Equal("USED", await f.Sql(f.Database.MockConnection,
            "SELECT status FROM mock_tickets WHERE id = @id", ("id", GateDemoSeedTicket)));
        Assert.Equal(true, await f.Sql(f.Database.MockConnection,
            "SELECT event_start_at > now() - interval '1 day' FROM mock_tickets WHERE id = @id", ("id", GateDemoSeedTicket)));
    }

    [Fact]
    public async Task Core_restart_in_keep_mode_keeps_listings_settings_and_users_but_moves_the_demo_dates()
    {
        // An old demo listing without money history: the Reset mode deletes it (DatabaseSeederCleanupTests).
        var listingId = Guid.NewGuid();
        await f.Sql(f.Database.CoreConnection,
            """
            INSERT INTO resale_listings
            SELECT (jsonb_populate_record(NULL::resale_listings, to_jsonb(l) ||
                jsonb_build_object('id', @listing, 'original_ticket_code', 'ATSH-GA-002',
                                   'listing_status', 'Cancelled', 'private_access_token', NULL))).*
            FROM resale_listings l WHERE l.id = @template
            """,
            ("listing", listingId), ("template", TemplateListing));
        await f.Sql(f.Database.CoreConnection,
            "UPDATE system_settings SET setting_value = '0.07' WHERE setting_key = 'ResaleFee_BuyerPercentage'");
        await f.Sql(f.Database.CoreConnection,
            "UPDATE shadow_users SET is_active = false WHERE id = @id", ("id", DemoModerator));
        await f.Sql(f.Database.CoreConnection,
            "UPDATE events SET event_start_at = now() - interval '10 days', name = 'Renamed by admin' WHERE id = @id",
            ("id", MarketplaceEvent));

        using (var scope = f.Core.Services.CreateScope())
        {
            await DatabaseSeeder.SeedTicketShieldAsync(
                scope.ServiceProvider.GetRequiredService<TicketShieldDbContext>(), keepExistingData: true);
        }

        Assert.Equal(1L, await f.Sql(f.Database.CoreConnection,
            "SELECT count(*) FROM resale_listings WHERE id = @id", ("id", listingId)));
        Assert.Equal("0.07", await f.Sql(f.Database.CoreConnection,
            "SELECT setting_value FROM system_settings WHERE setting_key = 'ResaleFee_BuyerPercentage'"));
        Assert.Equal(false, await f.Sql(f.Database.CoreConnection,
            "SELECT is_active FROM shadow_users WHERE id = @id", ("id", DemoModerator)));
        Assert.Equal("Renamed by admin", await f.Sql(f.Database.CoreConnection,
            "SELECT name FROM events WHERE id = @id", ("id", MarketplaceEvent)));
        Assert.Equal(true, await f.Sql(f.Database.CoreConnection,
            "SELECT event_start_at > now() FROM events WHERE id = @id", ("id", MarketplaceEvent)));
    }
}
