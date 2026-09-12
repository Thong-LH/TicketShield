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
            ALTER TABLE users ADD COLUMN IF NOT EXISTS password_hash VARCHAR(255);
            ALTER TABLE users ADD COLUMN IF NOT EXISTS google_id VARCHAR(255);
            ALTER TABLE users ADD COLUMN IF NOT EXISTS password_reset_otp VARCHAR(20);
            ALTER TABLE users ADD COLUMN IF NOT EXISTS password_reset_otp_expires_at TIMESTAMPTZ;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE organizers ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE events ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE ticket_tiers ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE resale_listings ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE escrow_transactions ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE payout_transactions ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE disputes ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;
        ");

        var defaultPasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");

        // 2. Seed / Sync Users
        var sellerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var buyerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var adminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var seller = await context.Users.FirstOrDefaultAsync(u => u.Id == sellerId);
        if (seller == null)
        {
            seller = new User { Id = sellerId };
            await context.Users.AddAsync(seller);
        }
        seller.Email = "linhtranlatao2004@gmail.com";
        seller.FullName = "Nguyen Van Seller";
        seller.PhoneNumber = "0901234567";
        seller.PasswordHash = defaultPasswordHash;
        seller.Role = UserRole.User;
        seller.IsActive = true;

        var buyer = await context.Users.FirstOrDefaultAsync(u => u.Id == buyerId);
        if (buyer == null)
        {
            buyer = new User { Id = buyerId };
            await context.Users.AddAsync(buyer);
        }
        buyer.Email = "buyer@ticketshield.vn";
        buyer.FullName = "Tran Thi Buyer";
        buyer.PhoneNumber = "0987654321";
        buyer.PasswordHash = defaultPasswordHash;
        buyer.Role = UserRole.User;
        buyer.IsActive = true;

        var admin = await context.Users.FirstOrDefaultAsync(u => u.Id == adminId);
        if (admin == null)
        {
            admin = new User { Id = adminId };
            await context.Users.AddAsync(admin);
        }
        admin.Email = "admin@ticketshield.vn";
        admin.FullName = "System Administrator";
        admin.PhoneNumber = "0999999999";
        admin.PasswordHash = defaultPasswordHash;
        admin.Role = UserRole.Admin;
        admin.IsActive = true;

        await context.SaveChangesAsync();

        // 3. Seed / Sync Organizer, Concert Event & Ticket Tiers
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

        var ev = await context.Events.FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev == null)
        {
            ev = new Event { Id = eventId };
            await context.Events.AddAsync(ev);
        }
        ev.OrganizerId = organizerId;
        ev.Name = "Anh Trai Say Hi Concert 2026";
        ev.Description = "Mega Concert Vietnam 2026";
        ev.Venue = "Van Hanh Mall Stadium, TP.HCM";
        ev.EventStartAt = DateTimeOffset.UtcNow.AddDays(30);
        ev.EventEndAt = DateTimeOffset.UtcNow.AddDays(30).AddHours(4);
        ev.ResaleDeadline = DateTimeOffset.UtcNow.AddDays(30).AddHours(-2);
        ev.Status = "UPCOMING";

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

        // Delete any non-seed users if present
        var nonSeedUsers = await context.Users
            .Where(u => u.Id != sellerId && u.Id != buyerId && u.Id != adminId)
            .ToListAsync();
        if (nonSeedUsers.Any())
        {
            context.Users.RemoveRange(nonSeedUsers);
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
        sampleListing.IsPrivate = false;
        sampleListing.PrivateAccessToken = null;
        sampleListing.VerificationStatus = VerificationStatus.Verified;
        sampleListing.ListingStatus = ListingStatus.Verified;

        await context.SaveChangesAsync();
    }
}
