using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Domain.Entities;
using TicketShield.Identity.Domain.Enums;

namespace TicketShield.Identity.Infrastructure.Persistence;

public static class IdentityDatabaseSeeder
{
    public static async Task SeedAsync(IdentityDbContext context)
    {
        // Tự động tạo bảng & cấu trúc nếu chưa có
        await context.Database.EnsureCreatedAsync();
        if (context.Database.IsRelational())
        {
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS trust_score integer NOT NULL DEFAULT 100;");
        }

        var defaultPasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");

        var sellerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var buyerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var adminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        // 1. Seller
        var seller = await context.Users.Include(u => u.BankAccounts).FirstOrDefaultAsync(u => u.Id == sellerId);
        if (seller == null)
        {
            seller = new User { Id = sellerId };
            await context.Users.AddAsync(seller);
        }
        seller.Email = "linhtranlatao2004@gmail.com";
        seller.FullName = "Nguyen Van Seller";
        seller.PhoneNumber = "0901234567";
        seller.IdCardNumber = "079204001234";
        seller.PasswordHash = defaultPasswordHash;
        seller.Role = UserRole.User;
        seller.IsActive = true;
        if (!seller.BankAccounts.Any())
        {
            seller.BankAccounts.Add(new UserBankAccount
            {
                BankCode = "MB",
                BankAccountNumber = "0901234567",
                AccountHolderName = "NGUYEN VAN SELLER",
                IsDefault = true
            });
        }

        // 2. Buyer
        var buyer = await context.Users.Include(u => u.BankAccounts).FirstOrDefaultAsync(u => u.Id == buyerId);
        if (buyer == null)
        {
            buyer = new User { Id = buyerId };
            await context.Users.AddAsync(buyer);
        }
        buyer.Email = "buyer@ticketshield.vn";
        buyer.FullName = "Tran Thi Buyer";
        buyer.PhoneNumber = "0907654321";
        buyer.PasswordHash = defaultPasswordHash;
        buyer.Role = UserRole.User;
        buyer.IsActive = true;
        if (!buyer.BankAccounts.Any())
        {
            buyer.BankAccounts.Add(new UserBankAccount
            {
                BankCode = "VCB",
                BankAccountNumber = "0071009876543",
                AccountHolderName = "TRAN THI BUYER",
                IsDefault = true
            });
        }

        // 3. Admin
        var admin = await context.Users.FirstOrDefaultAsync(u => u.Id == adminId);
        if (admin == null)
        {
            admin = new User { Id = adminId };
            await context.Users.AddAsync(admin);
        }
        admin.Email = "admin@ticketshield.vn";
        admin.FullName = "System Administrator";
        admin.PhoneNumber = "0909999999";
        admin.IdCardNumber = "001099000001";
        admin.PasswordHash = defaultPasswordHash;
        admin.Role = UserRole.Admin;
        admin.IsActive = true;

        // 4. Moderator / CSKH
        var moderatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var moderator = await context.Users.FirstOrDefaultAsync(u => u.Id == moderatorId);
        if (moderator == null)
        {
            moderator = new User { Id = moderatorId };
            await context.Users.AddAsync(moderator);
        }
        moderator.Email = "moderator@ticketshield.vn";
        moderator.FullName = "Le Thi CSKH & Moderator";
        moderator.PhoneNumber = "0908888888";
        moderator.PasswordHash = defaultPasswordHash;
        moderator.Role = UserRole.Moderator;
        moderator.IsActive = true;

        // 5. Organizer
        var organizerId = Guid.Parse("e0000000-0000-0000-0000-000000000001");
        var organizer = await context.Users.Include(u => u.BankAccounts).FirstOrDefaultAsync(u => u.Id == organizerId);
        if (organizer == null)
        {
            organizer = new User { Id = organizerId };
            await context.Users.AddAsync(organizer);
        }
        organizer.Email = "organizer@skymusic.vn";
        organizer.FullName = "SkyMusic Entertainment BTC";
        organizer.PhoneNumber = "0912345678";
        organizer.IdCardNumber = "079099887766";
        organizer.PasswordHash = defaultPasswordHash;
        organizer.Role = UserRole.Organizer;
        organizer.IsActive = true;
        if (!organizer.BankAccounts.Any())
        {
            organizer.BankAccounts.Add(new UserBankAccount
            {
                BankCode = "VCB",
                BankAccountNumber = "9988776655",
                AccountHolderName = "CONG TY CP SKYMUSIC ENT",
                IsDefault = true
            });
        }

        // 6. Trader
        var traderId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var trader = await context.Users.Include(u => u.BankAccounts).FirstOrDefaultAsync(u => u.Id == traderId);
        if (trader == null)
        {
            trader = new User { Id = traderId };
            await context.Users.AddAsync(trader);
        }
        trader.Email = "hoangnam.trader@gmail.com";
        trader.FullName = "Hoang Van Nam (Trader)";
        trader.PhoneNumber = "0933221100";
        trader.IdCardNumber = "079199223344";
        trader.PasswordHash = defaultPasswordHash;
        trader.Role = UserRole.User;
        trader.IsActive = true;
        if (!trader.BankAccounts.Any())
        {
            trader.BankAccounts.Add(new UserBankAccount
            {
                BankCode = "TCB",
                BankAccountNumber = "19034567890123",
                AccountHolderName = "HOANG VAN NAM",
                IsDefault = true
            });
        }

        // 7. Suspended User
        var suspendedId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var suspended = await context.Users.FirstOrDefaultAsync(u => u.Id == suspendedId);
        if (suspended == null)
        {
            suspended = new User { Id = suspendedId };
            await context.Users.AddAsync(suspended);
        }
        suspended.Email = "minhanh.music@yahoo.com";
        suspended.FullName = "Pham Minh Anh";
        suspended.PhoneNumber = "0977665544";
        suspended.PasswordHash = defaultPasswordHash;
        suspended.Role = UserRole.User;
        suspended.IsActive = false;
        suspended.TrustScore = 0;

        await context.SaveChangesAsync();
    }
}
