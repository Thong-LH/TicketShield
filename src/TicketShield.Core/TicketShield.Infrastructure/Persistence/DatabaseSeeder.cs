using Microsoft.EntityFrameworkCore;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedTicketShieldAsync(TicketShieldDbContext context)
    {
        // 1. Auto-apply any pending migrations and ensure auth columns exist
        await context.Database.MigrateAsync();

        await context.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE IF EXISTS users ADD COLUMN IF NOT EXISTS password_hash VARCHAR(255);
            ALTER TABLE IF EXISTS users ADD COLUMN IF NOT EXISTS google_id VARCHAR(255);
            ALTER TABLE IF EXISTS users ADD COLUMN IF NOT EXISTS password_reset_otp VARCHAR(20);
            ALTER TABLE IF EXISTS users ADD COLUMN IF NOT EXISTS password_reset_otp_expires_at TIMESTAMPTZ;
            ALTER TABLE IF EXISTS users ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE organizers ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE events ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE events ADD COLUMN IF NOT EXISTS artist VARCHAR(255);
            ALTER TABLE events ADD COLUMN IF NOT EXISTS category VARCHAR(50) DEFAULT 'CONCERT';
            ALTER TABLE events ADD COLUMN IF NOT EXISTS city VARCHAR(100) DEFAULT 'TP. Hồ Chí Minh';
            ALTER TABLE events ADD COLUMN IF NOT EXISTS banner_url VARCHAR(500);
            ALTER TABLE ticket_tiers ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE resale_listings ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE escrow_transactions ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE payout_transactions ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE disputes ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            CREATE TABLE IF NOT EXISTS system_settings (
                id UUID PRIMARY KEY,
                setting_key VARCHAR(100) NOT NULL UNIQUE,
                setting_value VARCHAR(500) NOT NULL,
                data_type VARCHAR(50) NOT NULL,
                description VARCHAR(500) NULL,
                updated_by UUID NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE TABLE IF NOT EXISTS shadow_users (
                id UUID PRIMARY KEY,
                email VARCHAR(255) NOT NULL,
                full_name VARCHAR(255) NOT NULL,
                phone_number VARCHAR(50) NULL,
                role VARCHAR(50) NOT NULL DEFAULT 'User',
                is_active BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_shadow_users_email ON shadow_users (email);

            -- Drop hard foreign key constraints to users(id) to allow database-per-service isolation
            ALTER TABLE resale_listings DROP CONSTRAINT IF EXISTS fk_resale_listings_users_seller_id;
            ALTER TABLE resale_listings DROP CONSTRAINT IF EXISTS resale_listings_seller_id_fkey;
            ALTER TABLE escrow_transactions DROP CONSTRAINT IF EXISTS fk_escrow_transactions_users_buyer_id;
            ALTER TABLE escrow_transactions DROP CONSTRAINT IF EXISTS fk_escrow_transactions_users_seller_id;
            ALTER TABLE escrow_transactions DROP CONSTRAINT IF EXISTS escrow_transactions_buyer_id_fkey;
            ALTER TABLE escrow_transactions DROP CONSTRAINT IF EXISTS escrow_transactions_seller_id_fkey;
            ALTER TABLE disputes DROP CONSTRAINT IF EXISTS fk_disputes_users_buyer_id;
            ALTER TABLE disputes DROP CONSTRAINT IF EXISTS fk_disputes_users_resolved_by;
            ALTER TABLE disputes DROP CONSTRAINT IF EXISTS disputes_buyer_id_fkey;
            ALTER TABLE disputes DROP CONSTRAINT IF EXISTS disputes_resolved_by_fkey;
            ALTER TABLE dispute_evidences DROP CONSTRAINT IF EXISTS fk_dispute_evidences_users_uploader_id;
            ALTER TABLE dispute_evidences DROP CONSTRAINT IF EXISTS dispute_evidences_uploader_id_fkey;
            ALTER TABLE dispute_messages DROP CONSTRAINT IF EXISTS fk_dispute_messages_users_sender_id;
            ALTER TABLE dispute_messages DROP CONSTRAINT IF EXISTS dispute_messages_sender_id_fkey;

            -- Backfill existing users into shadow_users if users table exists
            DO $$ 
            BEGIN 
                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'users') THEN
                    INSERT INTO shadow_users (id, email, full_name, phone_number, role, is_active, created_at, updated_at)
                    SELECT id, email, full_name, phone_number, role::text, is_active, created_at, updated_at
                    FROM users
                    ON CONFLICT (id) DO UPDATE SET
                        email = EXCLUDED.email,
                        full_name = EXCLUDED.full_name,
                        phone_number = EXCLUDED.phone_number,
                        role = EXCLUDED.role,
                        is_active = EXCLUDED.is_active,
                        updated_at = EXCLUDED.updated_at;
                END IF;
            END $$;
        ");

        var defaultPasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");

        // 2. Seed / Sync ShadowUsers for Trading Core
        var sellerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var buyerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var adminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var shadowSeller = await context.ShadowUsers.FirstOrDefaultAsync(u => u.Id == sellerId);
        if (shadowSeller == null)
        {
            shadowSeller = new ShadowUser { Id = sellerId };
            await context.ShadowUsers.AddAsync(shadowSeller);
        }
        shadowSeller.Email = "linhtranlatao2004@gmail.com";
        shadowSeller.FullName = "Nguyen Van Seller";
        shadowSeller.PhoneNumber = "0901234567";
        shadowSeller.Role = UserRole.User;
        shadowSeller.IsActive = true;

        var shadowBuyer = await context.ShadowUsers.FirstOrDefaultAsync(u => u.Id == buyerId);
        if (shadowBuyer == null)
        {
            shadowBuyer = new ShadowUser { Id = buyerId };
            await context.ShadowUsers.AddAsync(shadowBuyer);
        }
        shadowBuyer.Email = "buyer@ticketshield.vn";
        shadowBuyer.FullName = "Tran Thi Buyer";
        shadowBuyer.PhoneNumber = "0987654321";
        shadowBuyer.Role = UserRole.User;
        shadowBuyer.IsActive = true;

        var shadowAdmin = await context.ShadowUsers.FirstOrDefaultAsync(u => u.Id == adminId);
        if (shadowAdmin == null)
        {
            shadowAdmin = new ShadowUser { Id = adminId };
            await context.ShadowUsers.AddAsync(shadowAdmin);
        }
        shadowAdmin.Email = "admin@ticketshield.vn";
        shadowAdmin.FullName = "System Administrator";
        shadowAdmin.PhoneNumber = "0999999999";
        shadowAdmin.Role = UserRole.Admin;
        shadowAdmin.IsActive = true;

        await context.SaveChangesAsync();

        // 3. Seed / Sync Organizer, Concert Events & Ticket Tiers
        var organizerId = Guid.Parse("e0000000-0000-0000-0000-000000000001");
        var eventId = Guid.Parse("e1111111-1111-1111-1111-111111111111");
        var tierVipId = Guid.Parse("d1111111-1111-1111-1111-111111111111");
        var tierGaId = Guid.Parse("d2222222-2222-2222-2222-222222222222");

        var organizer = await context.Organizers.FirstOrDefaultAsync(o => o.Id == organizerId);
        if (organizer == null)
        {
            organizer = new Organizer { Id = organizerId };
            await context.Organizers.AddAsync(organizer);
        }
        organizer.Name = "VieON Entertainment";
        organizer.OfficialEmail = "contact@vieon.vn";
        organizer.ContactPhone = "19001234";
        organizer.ApiKeyHash = "mock_api_key_hash_123456";
        organizer.Status = "ACTIVE";

        // Event 1: Anh Trai Say Hi
        var ev = await context.Events.FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev == null)
        {
            ev = new Event { Id = eventId };
            await context.Events.AddAsync(ev);
        }
        ev.OrganizerId = organizerId;
        ev.Name = "Anh Trai Say Hi Concert 2026";
        ev.Artist = "Anh Trai Say Hi All-Stars";
        ev.Category = "CONCERT";
        ev.City = "TP. Hồ Chí Minh";
        ev.BannerUrl = "https://images.unsplash.com/photo-1470225620780-dba8ba36b745?auto=format&fit=crop&w=1400&q=80";
        ev.Description = "Mega Concert Vietnam 2026 quy tụ dàn nghệ sĩ đỉnh cao";
        ev.Venue = "Van Hanh Mall Stadium, TP.HCM";
        ev.EventStartAt = DateTimeOffset.UtcNow.AddDays(30);
        ev.EventEndAt = DateTimeOffset.UtcNow.AddDays(30).AddHours(4);
        ev.ResaleDeadline = DateTimeOffset.UtcNow.AddDays(30).AddHours(-2);
        ev.Status = "UPCOMING";
        ev.MaxResaleMarkupPercentage = 10m;

        var tierVip = await context.TicketTiers.FirstOrDefaultAsync(t => t.Id == tierVipId);
        if (tierVip == null)
        {
            tierVip = new TicketTier { Id = tierVipId };
            await context.TicketTiers.AddAsync(tierVip);
        }
        tierVip.EventId = eventId;
        tierVip.TierName = "VIP Zone A";
        tierVip.OriginalPrice = 2500000m;
        tierVip.Description = "Khu vực VIP sát sân khấu, tặng kèm lightstick";

        var tierGa = await context.TicketTiers.FirstOrDefaultAsync(t => t.Id == tierGaId);
        if (tierGa == null)
        {
            tierGa = new TicketTier { Id = tierGaId };
            await context.TicketTiers.AddAsync(tierGa);
        }
        tierGa.EventId = eventId;
        tierGa.TierName = "GA Standing";
        tierGa.OriginalPrice = 1200000m;
        tierGa.Description = "Khu vực đứng tự do";

        // Event 2: My Tam Live Concert (Hanoi)
        var event2Id = Guid.Parse("e2222222-2222-2222-2222-222222222222");
        var ev2 = await context.Events.FirstOrDefaultAsync(e => e.Id == event2Id);
        if (ev2 == null)
        {
            ev2 = new Event { Id = event2Id };
            await context.Events.AddAsync(ev2);
        }
        ev2.OrganizerId = organizerId;
        ev2.Name = "Tri Âm Live Concert 2026";
        ev2.Artist = "Mỹ Tâm";
        ev2.Category = "CONCERT";
        ev2.City = "Hà Nội";
        ev2.BannerUrl = "https://images.unsplash.com/photo-1514525253161-7a46d19cd819?auto=format&fit=crop&w=1400&q=80";
        ev2.Description = "Live Concert âm nhạc kỷ niệm đặc biệt tại thủ đô";
        ev2.Venue = "Sân vận động Mỹ Đình, Hà Nội";
        ev2.EventStartAt = DateTimeOffset.UtcNow.AddDays(15);
        ev2.EventEndAt = DateTimeOffset.UtcNow.AddDays(15).AddHours(4);
        ev2.ResaleDeadline = DateTimeOffset.UtcNow.AddDays(15).AddHours(-2);
        ev2.Status = "UPCOMING";

        var tier2GaId = Guid.Parse("d3333333-3333-3333-3333-333333333333");
        var tier2Ga = await context.TicketTiers.FirstOrDefaultAsync(t => t.Id == tier2GaId);
        if (tier2Ga == null)
        {
            tier2Ga = new TicketTier { Id = tier2GaId };
            await context.TicketTiers.AddAsync(tier2Ga);
        }
        tier2Ga.EventId = event2Id;
        tier2Ga.TierName = "Khán đài A";
        tier2Ga.OriginalPrice = 1500000m;
        tier2Ga.Description = "Ghế ngồi khán đài chính diện sân khấu";

        // Event 3: Ravolution EDM Festival
        var event3Id = Guid.Parse("e3333333-3333-3333-3333-333333333333");
        var ev3 = await context.Events.FirstOrDefaultAsync(e => e.Id == event3Id);
        if (ev3 == null)
        {
            ev3 = new Event { Id = event3Id };
            await context.Events.AddAsync(ev3);
        }
        ev3.OrganizerId = organizerId;
        ev3.Name = "Ravolution Music Festival 2026";
        ev3.Artist = "International DJ Lineup";
        ev3.Category = "FESTIVAL";
        ev3.City = "TP. Hồ Chí Minh";
        ev3.BannerUrl = "https://images.unsplash.com/photo-1501386761578-eac5c94b800a?auto=format&fit=crop&w=1400&q=80";
        ev3.Description = "Lễ hội âm nhạc điện tử ngoài trời lớn nhất năm";
        ev3.Venue = "SECC Quận 7, TP.HCM";
        ev3.EventStartAt = DateTimeOffset.UtcNow.AddDays(20);
        ev3.EventEndAt = DateTimeOffset.UtcNow.AddDays(20).AddHours(8);
        ev3.ResaleDeadline = DateTimeOffset.UtcNow.AddDays(20).AddHours(-2);
        ev3.Status = "UPCOMING";

        var tier3GaId = Guid.Parse("d4444444-4444-4444-4444-444444444444");
        var tier3Ga = await context.TicketTiers.FirstOrDefaultAsync(t => t.Id == tier3GaId);
        if (tier3Ga == null)
        {
            tier3Ga = new TicketTier { Id = tier3GaId };
            await context.TicketTiers.AddAsync(tier3Ga);
        }
        tier3Ga.EventId = event3Id;
        tier3Ga.TierName = "General Admission";
        tier3Ga.OriginalPrice = 850000m;
        tier3Ga.Description = "Vé vào cổng tự do";

        // Event 4: V-League Derby
        var event4Id = Guid.Parse("e4444444-4444-4444-4444-444444444444");
        var ev4 = await context.Events.FirstOrDefaultAsync(e => e.Id == event4Id);
        if (ev4 == null)
        {
            ev4 = new Event { Id = event4Id };
            await context.Events.AddAsync(ev4);
        }
        ev4.OrganizerId = organizerId;
        ev4.Name = "Trận Derby: CLB Hà Nội vs CLB Viettel";
        ev4.Artist = "V-League 2026";
        ev4.Category = "SPORTS";
        ev4.City = "Hà Nội";
        ev4.BannerUrl = "https://images.unsplash.com/photo-1508098682722-e99c43a406b2?auto=format&fit=crop&w=1400&q=80";
        ev4.Description = "Trận cầu đinh giải vô địch bóng đá quốc gia";
        ev4.Venue = "Sân vận động Hàng Đẫy, Hà Nội";
        ev4.EventStartAt = DateTimeOffset.UtcNow.AddDays(7);
        ev4.EventEndAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(2);
        ev4.ResaleDeadline = DateTimeOffset.UtcNow.AddDays(7).AddHours(-2);
        ev4.Status = "UPCOMING";

        var tier4GaId = Guid.Parse("d5555555-5555-5555-5555-555555555555");
        var tier4Ga = await context.TicketTiers.FirstOrDefaultAsync(t => t.Id == tier4GaId);
        if (tier4Ga == null)
        {
            tier4Ga = new TicketTier { Id = tier4GaId };
            await context.TicketTiers.AddAsync(tier4Ga);
        }
        tier4Ga.EventId = event4Id;
        tier4Ga.TierName = "Khán đài B";
        tier4Ga.OriginalPrice = 200000m;
        tier4Ga.Description = "Ghế ngồi khán đài B";

        await context.SaveChangesAsync();


        // 4. Reset & Purge extraneous operational data (Disputes, Escrows, Non-Seed Listings)
        var sampleListingId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        // Clear dependent tables first
        context.DisputeMessages.RemoveRange(await context.DisputeMessages.ToListAsync());
        context.DisputeEvidences.RemoveRange(await context.DisputeEvidences.ToListAsync());
        context.Disputes.RemoveRange(await context.Disputes.ToListAsync());
        context.PayoutTransactions.RemoveRange(await context.PayoutTransactions.ToListAsync());
        context.EscrowTransactions.RemoveRange(await context.EscrowTransactions.ToListAsync());

        // Delete all listings except sample listing (ATSH-GA-999)
        var staleListings = await context.ResaleListings
            .Where(l => l.Id != sampleListingId)
            .ToListAsync();
        if (staleListings.Any())
        {
            context.ResaleListings.RemoveRange(staleListings);
        }

        // Delete any non-seed shadow users if present
        var nonSeedShadowUsers = await context.ShadowUsers
            .Where(u => u.Id != sellerId && u.Id != buyerId && u.Id != adminId)
            .ToListAsync();
        if (nonSeedShadowUsers.Any())
        {
            context.ShadowUsers.RemoveRange(nonSeedShadowUsers);
        }

        await context.SaveChangesAsync();

        // 5. Seed / Reset Sample Resale Listing (ATSH-GA-999) for buyer marketplace page demo
        // ATSH-VIP-888 is intentionally unseeded so users can test the full OTP sell workflow from scratch.
        var sampleListing = await context.ResaleListings.FirstOrDefaultAsync(l => l.Id == sampleListingId);
        if (sampleListing == null)
        {
            sampleListing = new ResaleListing { Id = sampleListingId };
            await context.ResaleListings.AddAsync(sampleListing);
        }
        sampleListing.EventId = eventId;
        sampleListing.TierId = tierGaId;
        sampleListing.SellerId = sellerId;
        sampleListing.OriginalTicketCode = "ATSH-GA-999";
        sampleListing.OriginalPrice = 1200000m;
        sampleListing.ResalePrice = 1000000m;
        sampleListing.AppliedMarkupPercentage = 10m;
        sampleListing.IsPrivate = false;
        sampleListing.PrivateAccessToken = null;
        sampleListing.VerificationStatus = VerificationStatus.Verified;
        sampleListing.ListingStatus = ListingStatus.Verified;

        await context.SaveChangesAsync();

        // 6. Seed Default Resale Fee System Settings (BE-CORE-2.6.2)
        var feeSettings = new[]
        {
            new { Key = "ResaleFee_BuyerPercentage", Value = "0.05", Type = "Decimal", Desc = "Tỷ lệ phí người mua (5%)" },
            new { Key = "ResaleFee_SellerPercentage", Value = "0.03", Type = "Decimal", Desc = "Tỷ lệ phí người bán (3%)" },
            new { Key = "ResaleFee_MinBuyerFee", Value = "10000", Type = "Money", Desc = "Phí tối thiểu người mua (10,000 VNĐ)" },
            new { Key = "ResaleFee_MinSellerFee", Value = "5000", Type = "Money", Desc = "Phí tối thiểu người bán (5,000 VNĐ)" }
        };

        foreach (var item in feeSettings)
        {
            var setting = await context.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == item.Key);
            if (setting == null)
            {
                await context.SystemSettings.AddAsync(new SystemSetting
                {
                    SettingKey = item.Key,
                    SettingValue = item.Value,
                    DataType = item.Type,
                    Description = item.Desc
                });
            }
        }

        await context.SaveChangesAsync();
    }
}
