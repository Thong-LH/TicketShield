using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Controllers;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;

namespace TicketShield.UnitTests.Features.GateAccessLogs;

public class GateAccessLogsControllerTests
{
    [Fact]
    public async Task GetAccessLogs_WhenTicketHasTwoScans_ReturnsOnlyThatTicketOldestFirst()
    {
        await using var db = CreateDb();
        var asked = "ATSH-USED-001";
        var older = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var newer = older.AddHours(2);
        db.GateAccessLogs.AddRange(
            Log(asked, newer, "Cổng B", "dev-b", "DUPLICATE_ENTRY"),
            Log("OTHER-1", older, "Cổng Z", "dev-z", "SUCCESS"),
            Log(asked, older, "Cổng A", "dev-a", "SUCCESS"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GateAccessLogsController(db).GetAccessLogs(asked, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var logs = Assert.IsAssignableFrom<IReadOnlyList<GateAccessLog>>(ok.Value);
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal(2, logs.Count);
        Assert.Equal(new[] { older, newer }, logs.Select(row => row.ScannedAt).ToArray());
        Assert.All(logs, row => Assert.Equal(asked, row.TicketCode));
        Assert.Equal("Cổng A", logs[0].GateName);
        Assert.Equal("dev-a", logs[0].ScannerDeviceId);
        Assert.Equal("SUCCESS", logs[0].ScanResult);
        Assert.Equal("Cổng B", logs[1].GateName);
        Assert.Equal("dev-b", logs[1].ScannerDeviceId);
        Assert.Equal("DUPLICATE_ENTRY", logs[1].ScanResult);
    }

    [Fact]
    public async Task GetAccessLogs_WhenScansShareATime_OrdersByIdAscending()
    {
        await using var db = CreateDb();
        var when = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        db.GateAccessLogs.AddRange(
            Log("ATSH-USED-001", when, "Cổng B", "dev-b", "INVALID_TICKET", secondId),
            Log("ATSH-USED-001", when, "Cổng A", "dev-a", "REVOKED", firstId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GateAccessLogsController(db).GetAccessLogs("ATSH-USED-001", CancellationToken.None);

        var logs = Assert.IsAssignableFrom<IReadOnlyList<GateAccessLog>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(new[] { firstId, secondId }, logs.Select(row => row.Id).ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task GetAccessLogs_WhenTicketCodeIsMissing_Returns400AndLeavesRows(string? ticketCode)
    {
        await using var db = CreateDb();
        db.GateAccessLogs.Add(Log("ATSH-USED-001", DateTimeOffset.UtcNow, "Cổng A", "dev-a", "SUCCESS"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GateAccessLogsController(db).GetAccessLogs(ticketCode, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        Assert.Equal(400, ((BadRequestResult)result).StatusCode);
        Assert.Equal(1, await db.GateAccessLogs.CountAsync());
    }

    [Fact]
    public async Task GetAccessLogs_WhenTicketWasNeverScanned_Returns200AndEmptyList()
    {
        await using var db = CreateDb();

        var result = await new GateAccessLogsController(db).GetAccessLogs("ATSH-NONE", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var logs = Assert.IsAssignableFrom<IReadOnlyList<GateAccessLog>>(ok.Value);
        Assert.Equal(200, ok.StatusCode);
        Assert.Empty(logs);
    }

    private static OrganizerDbContext CreateDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<OrganizerDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new OrganizerDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static GateAccessLog Log(
        string ticketCode,
        DateTimeOffset scannedAt,
        string gateName,
        string scannerDeviceId,
        string scanResult,
        Guid? id = null)
    {
        return new GateAccessLog
        {
            Id = id ?? Guid.NewGuid(),
            TicketCode = ticketCode,
            ScannedAt = scannedAt,
            GateName = gateName,
            ScannerDeviceId = scannerDeviceId,
            ScanResult = scanResult
        };
    }
}
