using System.Text.Json;
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

    public DbSet<Organizer> Organizers => Set<Organizer>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<TicketTier> TicketTiers => Set<TicketTier>();
    public DbSet<ResaleListing> ResaleListings => Set<ResaleListing>();
    public DbSet<EscrowTransaction> EscrowTransactions => Set<EscrowTransaction>();
    public DbSet<PayoutTransaction> PayoutTransactions => Set<PayoutTransaction>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<DisputeEvidence> DisputeEvidences => Set<DisputeEvidence>();
    public DbSet<DisputeMessage> DisputeMessages => Set<DisputeMessage>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<CoreResaleRow> CoreResaleRecords => Set<CoreResaleRow>();
    public DbSet<ShadowUser> ShadowUsers => Set<ShadowUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ShadowUser (Read-model for Trading Core synced from Identity Microservice)
        modelBuilder.Entity<ShadowUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email);
            entity.Property(e => e.Role).HasConversion<string>();
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
            entity.Property(e => e.MaxResaleMarkupPercentage).HasPrecision(5, 2);
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
            entity.Property(e => e.AppliedMarkupPercentage).HasPrecision(5, 2);
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

            // Index for fast lookup by private access token
            entity.HasIndex(e => e.PrivateAccessToken);

            // Index for marketplace listing query and sorting
            entity.HasIndex(e => new { e.IsPrivate, e.ListingStatus, e.CreatedAt })
                .IsDescending(false, false, true);

            entity.Ignore(l => l.EscrowTransaction);
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
            entity.Property(e => e.NewTicketCode).HasMaxLength(255);

            entity.HasOne(e => e.Listing)
                .WithMany(l => l.EscrowTransactions)
                .HasForeignKey(e => e.ListingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Buyer)
                .WithMany()
                .HasForeignKey(e => e.BuyerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Seller)
                .WithMany()
                .HasForeignKey(e => e.SellerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.BankTransactionReference)
                .IsUnique()
                .HasFilter("bank_transaction_reference IS NOT NULL");

            entity.HasIndex(e => e.PaymentReference);
            entity.HasIndex(e => new { e.Status, e.UnlockAt });
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
                .WithMany()
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

        // SystemSetting
        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SettingKey).IsUnique();
            entity.Property(e => e.SettingKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SettingValue).HasMaxLength(500).IsRequired();
            entity.Property(e => e.DataType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        // CoreResaleRecords (Document store for Resale Verification Saga)
        modelBuilder.Entity<CoreResaleRow>(entity =>
        {
            entity.ToTable("core_resale_records");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id").HasColumnType("text");
            entity.Property(e => e.Json).HasColumnName("Json").HasColumnType("jsonb");
        });

        // Automatic snake_case naming for PostgreSQL
        ApplySnakeCaseNaming(modelBuilder);
    }

    private static void ApplySnakeCaseNaming(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.ClrType == typeof(CoreResaleRow)) continue;

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

    public async Task<IDbContextTransactionProxy?> BeginAdvisoryLockTransactionAsync(long lockKey, CancellationToken cancellationToken = default)
    {
        if (Database.IsRelational())
        {
            var tx = await Database.BeginTransactionAsync(cancellationToken);
            await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
            return new DbContextTransactionProxy(tx);
        }
        return null;
    }

    public async Task EnsureShadowUserExistsAsync(Guid id, string email, string fullName, CancellationToken cancellationToken = default)
    {
        if (Database.IsRelational())
        {
            await Database.ExecuteSqlRawAsync(
                "INSERT INTO shadow_users (id, email, full_name, role, is_active, created_at, updated_at) VALUES ({0}, {1}, {2}, 'User', true, NOW(), NOW()) ON CONFLICT (id) DO NOTHING",
                [id, email, fullName],
                cancellationToken);
        }
        else
        {
            var user = await ShadowUsers.FindAsync([id], cancellationToken);
            if (user == null)
            {
                ShadowUsers.Add(new ShadowUser
                {
                    Id = id,
                    Email = email,
                    FullName = fullName,
                    Role = Domain.Enums.UserRole.User,
                    IsActive = true
                });
                await SaveChangesAsync(cancellationToken);
            }
        }
    }

    public async Task<T?> ReadCoreResaleRecordAsync<T>(string id, CancellationToken ct = default) where T : class
    {
        var row = await CoreResaleRecords.FindAsync([id], ct);
        return row is null ? null : JsonSerializer.Deserialize<T>(row.Json);
    }

    public async Task PutCoreResaleRecordAsync<T>(string id, T value, CancellationToken ct = default)
    {
        var row = await CoreResaleRecords.FindAsync([id], ct);
        if (row is null)
        {
            row = new CoreResaleRow { Id = id };
            CoreResaleRecords.Add(row);
        }
        row.Json = JsonSerializer.Serialize(value);
        await SaveChangesAsync(ct);
    }

    private sealed class DbContextTransactionProxy(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx) : IDbContextTransactionProxy
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => tx.CommitAsync(cancellationToken);
        public Task RollbackAsync(CancellationToken cancellationToken = default) => tx.RollbackAsync(cancellationToken);
        public ValueTask DisposeAsync() => tx.DisposeAsync();
    }
}

