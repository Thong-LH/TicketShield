using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Persistence;

/// <summary>
/// Startup data mode for the Identity seeder: "Keep" (the default at startup) leaves the demo
/// accounts as they are, "Reset" restores their password, lock state and bank accounts.
/// </summary>
public class IdentitySeederStartupModeTests
{
    private static readonly Guid DemoSeller = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Keep_mode_does_not_unlock_reset_or_refill_an_existing_demo_account()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var db = CreateDb(databaseName))
        {
            await IdentityDatabaseSeeder.SeedAsync(db);
        }
        await ChangeTheDemoSellerAsync(databaseName, removeBankAccount: true);

        await using (var db = CreateDb(databaseName))
        {
            await IdentityDatabaseSeeder.SeedAsync(db, keepExistingData: true);
        }

        await using var check = CreateDb(databaseName);
        var seller = await check.Users.Include(u => u.BankAccounts).SingleAsync(u => u.Id == DemoSeller);
        Assert.False(seller.IsActive);
        Assert.Equal("changed-by-the-user", seller.PasswordHash);
        Assert.Empty(seller.BankAccounts);
    }

    [Fact]
    public async Task Keep_mode_still_creates_missing_demo_accounts()
    {
        await using var db = CreateDb(Guid.NewGuid().ToString());

        await IdentityDatabaseSeeder.SeedAsync(db, keepExistingData: true);

        var seller = await db.Users.Include(u => u.BankAccounts).SingleAsync(u => u.Id == DemoSeller);
        Assert.True(seller.IsActive);
        Assert.Single(seller.BankAccounts);
    }

    [Fact]
    public async Task Reset_mode_restores_the_demo_account()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var db = CreateDb(databaseName))
        {
            await IdentityDatabaseSeeder.SeedAsync(db);
        }
        // Removing the bank account matters: re-adding it once failed (the new row has a preset Guid
        // key, so adding it through the user's collection made EF run an UPDATE on a missing row).
        await ChangeTheDemoSellerAsync(databaseName, removeBankAccount: true);

        await using (var db = CreateDb(databaseName))
        {
            await IdentityDatabaseSeeder.SeedAsync(db, keepExistingData: false);
        }

        await using var check = CreateDb(databaseName);
        var seller = await check.Users.Include(u => u.BankAccounts).SingleAsync(u => u.Id == DemoSeller);
        Assert.True(seller.IsActive);
        Assert.NotEqual("changed-by-the-user", seller.PasswordHash);
        Assert.Single(seller.BankAccounts);
    }

    /// Simulates what happens while the system runs: the user changes the password
    /// (and may remove the bank account) and an admin locks the account.
    private static async Task ChangeTheDemoSellerAsync(string databaseName, bool removeBankAccount)
    {
        await using var db = CreateDb(databaseName);
        var seller = await db.Users.Include(u => u.BankAccounts).SingleAsync(u => u.Id == DemoSeller);
        seller.IsActive = false;
        seller.PasswordHash = "changed-by-the-user";
        if (removeBankAccount) db.RemoveRange(seller.BankAccounts);
        await db.SaveChangesAsync();
    }

    private static IdentityDbContext CreateDb(string databaseName) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(databaseName).Options);
}
