using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TicketShield.Infrastructure.Resale;

public sealed class CoreResaleRow { public string Id { get; set; } = ""; public string Json { get; set; } = "{}"; }
public sealed class CoreResaleStore(DbContextOptions<CoreResaleStore> options) : DbContext(options)
{
    public DbSet<CoreResaleRow> Records => Set<CoreResaleRow>();
    protected override void OnModelCreating(ModelBuilder model) => model.Entity<CoreResaleRow>(e => {
        e.ToTable("core_resale_records"); e.HasKey(x => x.Id); e.Property(x => x.Json).HasColumnType("jsonb");
    });
    public async Task<T?> Read<T>(string id, CancellationToken ct) where T : class
    {
        var row = await Records.FindAsync([id], ct);
        return row is null ? null : JsonSerializer.Deserialize<T>(row.Json);
    }
    public async Task Put<T>(string id, T value, CancellationToken ct)
    {
        var row = await Records.FindAsync([id], ct);
        if (row is null) { row = new CoreResaleRow { Id = id }; Records.Add(row); }
        row.Json = JsonSerializer.Serialize(value);
    }
}
[DbContext(typeof(CoreResaleStore))]
[Migration("202609110002_CoreResaleRecords")]
public sealed class CoreResaleRecords : Migration
{
    protected override void Up(MigrationBuilder m) => m.CreateTable("core_resale_records",
        columns: t => new { Id = t.Column<string>("text", nullable: false), Json = t.Column<string>("jsonb", nullable: false) },
        constraints: t => t.PrimaryKey("PK_core_resale_records", x => x.Id));
    protected override void Down(MigrationBuilder m) => m.DropTable("core_resale_records");
}
public sealed class CoreSession
{
    public string Id { get; set; } = "";
    public string Seller { get; set; } = "";
    public string TicketCode { get; set; } = "";
    public string State { get; set; } = "RequestPending";
    public string? PendingOperationId { get; set; }
    public string? ChallengeJson { get; set; }
    public string? ReceiptJson { get; set; }
    public Guid? ListingId { get; set; }
    public string? PrivateAccessToken { get; set; }
    public long? Price { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class CoreOperation
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Seller { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string State { get; set; } = "Pending";
    public string? Error { get; set; }
    public string? Step { get; set; }
    public string? LockId { get; set; }
    public ulong LockGeneration { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
}
