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

        var defaultPasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");

        var sellerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var buyerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var adminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        // 1. Seller
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

        // 2. Buyer
        var buyer = await context.Users.FirstOrDefaultAsync(u => u.Id == buyerId);
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
        admin.PasswordHash = defaultPasswordHash;
        admin.Role = UserRole.Admin;
        admin.IsActive = true;

        await context.SaveChangesAsync();
    }
}
