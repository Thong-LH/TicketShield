using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Entities;

namespace TicketShield.Infrastructure.Persistence;

public class TicketShieldDbContext : DbContext, ITicketShieldDbContext
{
    public TicketShieldDbContext(DbContextOptions<TicketShieldDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserBankAccount> UserBankAccounts => Set<UserBankAccount>();
    public DbSet<Organizer> Organizers => Set<Organizer>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<TicketTier> TicketTiers => Set<TicketTier>();
    public DbSet<ResaleListing> ResaleListings => Set<ResaleListing>();
    public DbSet<EscrowTransaction> EscrowTransactions => Set<EscrowTransaction>();
    public DbSet<PayoutTransaction> PayoutTransactions => Set<PayoutTransaction>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<DisputeEvidence> DisputeEvidences => Set<DisputeEvidence>();
    public DbSet<DisputeMessage> DisputeMessages => Set<DisputeMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Role).HasConversion<string>();
        });

        // UserBankAccount
        modelBuilder.Entity<UserBankAccount>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.User)
                .WithMany(u => u.BankAccounts)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Organizer
        modelBuilder.Entity<Organizer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OfficialEmail).IsUnique();
        });

        // Event
        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Organizer)
                .WithMany(o => o.Events)
                .HasForeignKey(e => e.OrganizerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // TicketTier
        modelBuilder.Entity<TicketTier>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalPrice).HasPrecision(15, 2);
            entity.HasOne(e => e.Event)
                .WithMany(ev => ev.TicketTiers)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ResaleListing (P2P Core)
        modelBuilder.Entity<ResaleListing>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalPrice).HasPrecision(15, 2);
            entity.Property(e => e.ResalePrice).HasPrecision(15, 2);
            entity.Property(e => e.VerificationStatus).HasConversion<string>();
            entity.Property(e => e.ListingStatus).HasConversion<string>();

            entity.HasOne(e => e.Event)
                .WithMany(ev => ev.ResaleListings)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Tier)
                .WithMany(t => t.ResaleListings)
                .HasForeignKey(e => e.TierId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Seller)
                .WithMany(u => u.ResaleListings)
                .HasForeignKey(e => e.SellerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Partial Unique Index: Only 1 active listing per original ticket code
            entity.HasIndex(e => e.OriginalTicketCode)
                .IsUnique()
                .HasFilter("listing_status IN ('Verified', 'Transacting')");
        });

        // EscrowTransaction
        modelBuilder.Entity<EscrowTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalTicketPrice).HasPrecision(15, 2);
            entity.Property(e => e.BuyerFee).HasPrecision(15, 2);
            entity.Property(e => e.SellerFee).HasPrecision(15, 2);
            entity.Property(e => e.TotalBuyerPaid).HasPrecision(15, 2);
            entity.Property(e => e.NetSellerPayout).HasPrecision(15, 2);
            entity.Property(e => e.Status).HasConversion<string>();

            entity.HasOne(e => e.Listing)
                .WithOne(l => l.EscrowTransaction)
                .HasForeignKey<EscrowTransaction>(e => e.ListingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Buyer)
                .WithMany(u => u.PurchasedEscrows)
                .HasForeignKey(e => e.BuyerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Seller)
                .WithMany()
                .HasForeignKey(e => e.SellerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // PayoutTransaction
        modelBuilder.Entity<PayoutTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PayoutCode).IsUnique();
            entity.Property(e => e.Amount).HasPrecision(15, 2);
            entity.Property(e => e.Status).HasConversion<string>();

            entity.HasOne(e => e.Escrow)
                .WithOne(es => es.PayoutTransaction)
                .HasForeignKey<PayoutTransaction>(e => e.EscrowId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.SellerBankAccount)
                .WithMany(b => b.Payouts)
                .HasForeignKey(e => e.SellerBankAccountId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Dispute
        modelBuilder.Entity<Dispute>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DisputeCode).IsUnique();
            entity.Property(e => e.RefundAmount).HasPrecision(15, 2);
            entity.Property(e => e.ReasonCode).HasConversion<string>();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.Resolution).HasConversion<string>();

            entity.HasOne(e => e.Escrow)
                .WithOne(es => es.Dispute)
                .HasForeignKey<Dispute>(e => e.EscrowId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Buyer)
                .WithMany(u => u.Disputes)
                .HasForeignKey(e => e.BuyerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Resolver)
                .WithMany()
                .HasForeignKey(e => e.ResolvedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // DisputeEvidence
        modelBuilder.Entity<DisputeEvidence>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Dispute)
                .WithMany(d => d.Evidences)
                .HasForeignKey(e => e.DisputeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Uploader)
                .WithMany()
                .HasForeignKey(e => e.UploaderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // DisputeMessage
        modelBuilder.Entity<DisputeMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Dispute)
                .WithMany(d => d.Messages)
                .HasForeignKey(e => e.DisputeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Sender)
                .WithMany()
                .HasForeignKey(e => e.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Automatic snake_case naming for PostgreSQL
        ApplySnakeCaseNaming(modelBuilder);
    }

    private static void ApplySnakeCaseNaming(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnakeCase(entity.GetTableName() ?? entity.DisplayName()));

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName() ?? "pk"));
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName() ?? "fk"));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName() ?? "ix"));
            }
        }
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return Regex.Replace(input, @"([a-z0-9])([A-Z])", "$1_$2").ToLower();
    }
}
