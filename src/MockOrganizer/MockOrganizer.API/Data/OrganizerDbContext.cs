using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Entities;

namespace MockOrganizer.API.Data;

public class OrganizerDbContext : DbContext
{
    public OrganizerDbContext(DbContextOptions<OrganizerDbContext> options) : base(options)
    {
    }

    public DbSet<MockTicket> MockTickets => Set<MockTicket>();
    public DbSet<MockOtp> MockOtps => Set<MockOtp>();
    public DbSet<GateAccessLog> GateAccessLogs => Set<GateAccessLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MockTicket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TicketCode).IsUnique();
            entity.Property(e => e.OriginalPrice).HasPrecision(15, 2);
        });

        modelBuilder.Entity<MockOtp>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TicketCode, e.OwnerEmail, e.IsUsed });
        });

        modelBuilder.Entity<GateAccessLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TicketCode);
        });

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
