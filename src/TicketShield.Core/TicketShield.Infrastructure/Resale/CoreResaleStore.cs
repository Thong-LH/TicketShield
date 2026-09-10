using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TicketShield.Infrastructure.Resale;

/// <summary>
/// Bảng lưu trữ trạng thái phi cấu trúc (Saga state) của các phiên xác thực vé
/// </summary>
public sealed class CoreResaleRow
{
    public string Id { get; set; } = string.Empty;
    public string Json { get; set; } = "{}";
}

/// <summary>
/// DbContext chuyên biệt quản lý lưu trữ trạng thái phân tán (Saga state machine)
/// </summary>
public sealed class CoreResaleStore(DbContextOptions<CoreResaleStore> options) : DbContext(options)
{
    public DbSet<CoreResaleRow> Records => Set<CoreResaleRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CoreResaleRow>(e =>
        {
            e.ToTable("core_resale_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Json).HasColumnType("jsonb");
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
        if (row is null)
        {
            row = new CoreResaleRow { Id = id };
            Records.Add(row);
        }
        row.Json = JsonSerializer.Serialize(value);
    }
}

[DbContext(typeof(CoreResaleStore))]
[Migration("202609110002_CoreResaleRecords")]
public sealed class CoreResaleRecords : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable(
            name: "core_resale_records",
            columns: t => new
            {
                Id = t.Column<string>("text", nullable: false),
                Json = t.Column<string>("jsonb", nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_core_resale_records", x => x.Id));
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("core_resale_records");
    }
}

/// <summary>
/// Trạng thái phiên xác thực vé (Verification Session)
/// </summary>
public sealed class CoreSession
{
    public string Id { get; set; } = string.Empty;
    public string Seller { get; set; } = string.Empty;
    public string TicketCode { get; set; } = string.Empty;
    public string State { get; set; } = "RequestPending";
    public string? PendingOperationId { get; set; }
    public string? ChallengeJson { get; set; }
    public string? ReceiptJson { get; set; }
    public Guid? ListingId { get; set; }
    public string? PrivateAccessToken { get; set; }
    public long? Price { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Trạng thái tác vụ Idempotent (Idempotent Operation)
/// </summary>
public sealed class CoreOperation
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Seller { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string State { get; set; } = "Pending";
    public string? Error { get; set; }
    public string? Step { get; set; }
    public string? LockId { get; set; }
    public ulong LockGeneration { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
}
