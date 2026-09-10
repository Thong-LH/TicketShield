using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MockOrganizer.API.Entities;

namespace MockOrganizer.API.Resale;

public sealed class ResaleRow { public string Id { get; set; } = ""; public string Json { get; set; } = "{}"; }

public sealed class ResaleStore(DbContextOptions<ResaleStore> options) : DbContext(options)
{
    public DbSet<ResaleRow> Records => Set<ResaleRow>();
    public DbSet<MockTicket> Tickets => Set<MockTicket>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ResaleRow>(e => { e.ToTable("organizer_resale_records"); e.HasKey(x => x.Id); e.Property(x => x.Json).HasColumnType("jsonb"); });
        model.Entity<MockTicket>(e => {
            e.ToTable("mock_tickets", t => t.ExcludeFromMigrations()); e.HasKey(x => x.Id);
            foreach (var p in e.Metadata.GetProperties())
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
            e.Property(x => x.OriginalPrice).HasPrecision(15, 2);
        });
    }
    public async Task<T?> Read<T>(string id, CancellationToken ct) where T : class
    {
        var row = await Records.FindAsync([id], ct);
        return row is null ? null : JsonSerializer.Deserialize<T>(row.Json);
    }
    public async Task Put<T>(string id, T value, CancellationToken ct)
    {
        var row = await Records.FindAsync([id], ct);
        if (row is null) { row = new ResaleRow { Id = id }; Records.Add(row); }
        row.Json = JsonSerializer.Serialize(value);
    }
}

[DbContext(typeof(ResaleStore))]
[Migration("202609110001_OrganizerResaleRecords")]
public sealed class OrganizerResaleRecords : Migration
{
    protected override void Up(MigrationBuilder m) => m.CreateTable("organizer_resale_records",
        columns: t => new { Id = t.Column<string>("text", nullable: false), Json = t.Column<string>("jsonb", nullable: false) },
        constraints: t => t.PrimaryKey("PK_organizer_resale_records", x => x.Id));
    protected override void Down(MigrationBuilder m) => m.DropTable("organizer_resale_records");
}

public sealed class SessionState
{
    public string Id { get; set; } = "";
    public string Caller { get; set; } = "";
    public string Requester { get; set; } = "";
    public string TicketCode { get; set; } = "";
    public string ChallengeId { get; set; } = "";
    public uint Generation { get; set; }
    public string OtpVerifier { get; set; } = "";
    public string OwnerRevision { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset ResendAfter { get; set; }
    public int Failures { get; set; }
    public int Resends { get; set; }
    public bool Closed { get; set; }
    public string Delivery { get; set; } = "Pending";
    public string? ReceiptJson { get; set; }
    public string? LockId { get; set; }
}
public sealed class LockRecord
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Caller { get; set; } = "";
    public string Requester { get; set; } = "";
    public string TicketCode { get; set; } = "";
    public string OwnerRevision { get; set; } = "";
    public ulong Generation { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}
public sealed class OperationRecord
{
    public string Fingerprint { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public string? Error { get; set; }
    public int Status { get; set; }
    public string SessionId { get; set; } = "";
    public string Requester { get; set; } = "";
}
public sealed class Quota { public DateTimeOffset Since { get; set; } public int Count { get; set; } }
