using Microsoft.EntityFrameworkCore;

namespace TicketShield.Settlement.API.Data;

public class SettlementDbContext : DbContext
{
    public SettlementDbContext(DbContextOptions<SettlementDbContext> options) : base(options)
    {
    }

    public DbSet<TransferOrder> TransferOrders => Set<TransferOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<TransferOrder>();
        order.ToTable("transfer_orders");
        order.HasKey(row => row.Id);
        order.HasIndex(row => row.IdempotencyKey).IsUnique();
        order.Property(row => row.IdempotencyKey).HasMaxLength(80);
        order.Property(row => row.Amount).HasPrecision(15, 2);
        order.Property(row => row.BankCode).HasMaxLength(20);
        order.Property(row => row.AccountNumber).HasMaxLength(32);
        order.Property(row => row.AccountName).HasMaxLength(120);
        order.Property(row => row.BankReference).HasMaxLength(64);
        order.Property(row => row.LastError).HasMaxLength(200);
        order.Property(row => row.State).HasConversion<string>().HasMaxLength(20);
    }
}
