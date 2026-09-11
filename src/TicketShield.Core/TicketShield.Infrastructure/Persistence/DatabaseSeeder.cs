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
        ");

        var defaultPasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");

        // 2. Seed Users
        if (!await context.Users.AnyAsync())
        {
            var seller = new User
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Email = "seller@ticketshield.vn",
                FullName = "Nguyen Van Seller",
                PhoneNumber = "0901234567",
                PasswordHash = defaultPasswordHash,
                Role = UserRole.User,
                IsActive = true
            };

            var buyer = new User
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Email = "buyer@ticketshield.vn",
                FullName = "Tran Thi Buyer",
                PhoneNumber = "0987654321",
                PasswordHash = defaultPasswordHash,
                Role = UserRole.User,
                IsActive = true
            };

            var admin = new User
            {
                Id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                Email = "admin@ticketshield.vn",
                FullName = "System Administrator",
                PhoneNumber = "0999999999",
                PasswordHash = defaultPasswordHash,
                Role = UserRole.Admin,
                IsActive = true
            };

            await context.Users.AddRangeAsync(seller, buyer, admin);
        }
        else
        {
            // Backfill default password hash for existing seed users if null
            var usersWithoutPassword = await context.Users
                .Where(u => u.PasswordHash == null)
                .ToListAsync();

            if (usersWithoutPassword.Any())
            {
                foreach (var u in usersWithoutPassword)
                {
                    u.PasswordHash = defaultPasswordHash;
                }
                await context.SaveChangesAsync();
            }
        }

        // 3. Seed Organizer & Concert Events
        if (!await context.Organizers.AnyAsync())
        {
            var organizer = new Organizer
            {
                Id = Guid.Parse("e0000000-0000-0000-0000-000000000001"),
                Name = "VieON Entertainment",
                OfficialEmail = "contact@vieon.vn",
                ContactPhone = "19001234",
                ApiKeyHash = "mock_api_key_hash_123456",
                Status = "ACTIVE"
            };

            var ev = new Event
            {
                Id = Guid.Parse("e1111111-1111-1111-1111-111111111111"),
                OrganizerId = organizer.Id,
                Name = "Anh Trai Say Hi Concert 2026",
                Description = "Mega Concert Vietnam 2026",
                Venue = "Van Hanh Mall Stadium, TP.HCM",
                EventStartAt = DateTimeOffset.UtcNow.AddDays(30),
                EventEndAt = DateTimeOffset.UtcNow.AddDays(30).AddHours(4),
                ResaleDeadline = DateTimeOffset.UtcNow.AddDays(30).AddHours(-2),
                Status = "UPCOMING"
            };

            var tierVip = new TicketTier
            {
                Id = Guid.Parse("d1111111-1111-1111-1111-111111111111"),
                EventId = ev.Id,
                TierName = "VIP Zone A",
                OriginalPrice = 2500000,
                Description = "Khu vực VIP sát sân khấu, tặng kèm lighstick"
            };

            var tierGa = new TicketTier
            {
                Id = Guid.Parse("d2222222-2222-2222-2222-222222222222"),
                EventId = ev.Id,
                TierName = "GA Standing",
                OriginalPrice = 1200000,
                Description = "Khu vực đứng tự do"
            };

            await context.Organizers.AddAsync(organizer);
            await context.Events.AddAsync(ev);
            await context.TicketTiers.AddRangeAsync(tierVip, tierGa);
        }

        // 4. Seed Sample Resale Listings for seller@ticketshield.vn
        if (!await context.ResaleListings.AnyAsync())
        {
            var sellerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var eventId = Guid.Parse("e1111111-1111-1111-1111-111111111111");
            var tierVipId = Guid.Parse("d1111111-1111-1111-1111-111111111111");
            var tierGaId = Guid.Parse("d2222222-2222-2222-2222-222222222222");

            var samplePublicListing = new ResaleListing
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                EventId = eventId,
                TierId = tierVipId,
                SellerId = sellerId,
                OriginalTicketCode = "ATSH-VIP-888",
                OriginalPrice = 2500000m,
                ResalePrice = 2200000m,
                IsPrivate = false,
                PrivateAccessToken = null,
                VerificationStatus = VerificationStatus.Verified,
                ListingStatus = ListingStatus.Verified,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-3)
            };

            var samplePrivateListing = new ResaleListing
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                EventId = eventId,
                TierId = tierGaId,
                SellerId = sellerId,
                OriginalTicketCode = "ATSH-GA-999",
                OriginalPrice = 1200000m,
                ResalePrice = 1000000m,
                IsPrivate = true,
                PrivateAccessToken = "a1b2c3d4e5f67890123456789abcdef0",
                VerificationStatus = VerificationStatus.Verified,
                ListingStatus = ListingStatus.Verified,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
            };

            await context.ResaleListings.AddRangeAsync(samplePublicListing, samplePrivateListing);
        }

        await context.SaveChangesAsync();
    }
}
