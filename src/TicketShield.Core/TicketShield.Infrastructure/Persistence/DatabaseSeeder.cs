using Microsoft.EntityFrameworkCore;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedTicketShieldAsync(TicketShieldDbContext context)
    {
        // 1. Auto-apply any pending migrations on startup (code-first auto-init)
        await context.Database.MigrateAsync();

        // 2. Seed Users
        if (!await context.Users.AnyAsync())
        {
            var seller = new User
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Email = "seller@ticketshield.vn",
                FullName = "Nguyen Van Seller",
                PhoneNumber = "0901234567",
                Role = UserRole.User,
                IsActive = true
            };

            var buyer = new User
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Email = "buyer@ticketshield.vn",
                FullName = "Tran Thi Buyer",
                PhoneNumber = "0987654321",
                Role = UserRole.User,
                IsActive = true
            };

            var admin = new User
            {
                Id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                Email = "admin@ticketshield.vn",
                FullName = "System Administrator",
                PhoneNumber = "0999999999",
                Role = UserRole.Admin,
                IsActive = true
            };

            await context.Users.AddRangeAsync(seller, buyer, admin);
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

        await context.SaveChangesAsync();
    }
}
