using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MockOrganizer.API.Controllers;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;
using MockOrganizer.API.Gate;

namespace TicketShield.UnitTests.Features.GateAccessLogs;

public class GateScanServiceTests
{
    private const string Code = "ATSH-GA-999";

    private sealed class RecordingNotifier : ITicketUsedNotifier
    {
        private readonly Exception? _failure;

        public RecordingNotifier(Exception? failure = null) => _failure = failure;

        public List<string> Calls { get; } = new();

        public Task NotifyAsync(string ticketCode, CancellationToken cancellationToken)
        {
            lock (Calls)
            {
                Calls.Add(ticketCode);
            }

            return _failure == null ? Task.CompletedTask : Task.FromException(_failure);
        }
    }

    [Fact]
    public async Task Scan_WhenTicketIsValid_MarksUsedAndReturnsEventDetails()
    {
        await using var db = CreateDb();
        await AddTicketAsync(db, Code, "VALID");
        var notifier = new RecordingNotifier();

        var response = await Service(db, notifier).ScanAsync(Request("scan-1"), CancellationToken.None);

        Assert.Equal(GateScanResults.Success, response.Result);
        Assert.Equal("Concert", response.EventName);
        Assert.Equal("Zone A", response.SeatZone);
        Assert.Null(response.FirstScannedAt);
        Assert.Equal("USED", await StatusOfAsync(db, Code));
        var log = Assert.Single(await LogsAsync(db));
        Assert.Equal(GateScanResults.Success, log.ScanResult);
        Assert.Equal("Gate A", log.GateName);
        Assert.Equal("scan-1", log.ScanId);
        Assert.Equal(Code, Assert.Single(notifier.Calls));
    }

    [Fact]
    public async Task Scan_WhenTicketWasScannedBefore_ReturnsFirstScanDetails()
    {
        await using var db = CreateDb();
        await AddTicketAsync(db, Code, "VALID");
        var notifier = new RecordingNotifier();
        var service = Service(db, notifier);
        var first = await service.ScanAsync(Request("scan-1"), CancellationToken.None);

        var second = await service.ScanAsync(Request("scan-2", gateName: "Gate B"), CancellationToken.None);

        Assert.Equal(GateScanResults.Duplicate, second.Result);
        Assert.Equal(first.ScannedAt, second.FirstScannedAt);
        Assert.Equal("Gate A", second.FirstGateName);
        Assert.Equal(2, (await LogsAsync(db)).Count);
        Assert.Single(notifier.Calls);
    }

    [Fact]
    public async Task Scan_WhenSeedTicketIsUsedWithoutLog_ReturnsDuplicateWithoutFirstScan()
    {
        await using var db = CreateDb();
        await AddTicketAsync(db, Code, "USED");

        var response = await Service(db).ScanAsync(Request("scan-1"), CancellationToken.None);

        Assert.Equal(GateScanResults.Duplicate, response.Result);
        Assert.Null(response.FirstScannedAt);
        Assert.Null(response.FirstGateName);
    }

    [Theory]
    [InlineData("TRANSFERRED")]
    [InlineData("CANCELLED")]
    [InlineData("LOCKED_FOR_RESALE")]
    public async Task Scan_WhenTicketCannotEnter_ReturnsRevokedAndKeepsStatus(string status)
    {
        await using var db = CreateDb();
        await AddTicketAsync(db, Code, status);
        var notifier = new RecordingNotifier();

        var response = await Service(db, notifier).ScanAsync(Request("scan-1"), CancellationToken.None);

        Assert.Equal(GateScanResults.Revoked, response.Result);
        Assert.Equal(status, await StatusOfAsync(db, Code));
        Assert.Equal(GateScanResults.Revoked, Assert.Single(await LogsAsync(db)).ScanResult);
        Assert.Empty(notifier.Calls);
    }

    [Fact]
    public async Task Scan_WhenCodeDoesNotExist_ReturnsInvalidAndLogsIt()
    {
        await using var db = CreateDb();
        var notifier = new RecordingNotifier();

        var response = await Service(db, notifier).ScanAsync(Request("scan-1", "NOT-A-TICKET"), CancellationToken.None);

        Assert.Equal(GateScanResults.Invalid, response.Result);
        Assert.Null(response.EventName);
        Assert.Equal(GateScanResults.Invalid, Assert.Single(await LogsAsync(db)).ScanResult);
        Assert.Empty(notifier.Calls);
    }

    [Fact]
    public async Task Scan_WhenSameScanIdIsSentAgain_ReturnsSameResultWithoutNewLogOrWebhook()
    {
        await using var db = CreateDb();
        await AddTicketAsync(db, Code, "VALID");
        var notifier = new RecordingNotifier();
        var service = Service(db, notifier);
        var first = await service.ScanAsync(Request("scan-1"), CancellationToken.None);

        var again = await service.ScanAsync(Request("scan-1"), CancellationToken.None);

        Assert.Equal(GateScanResults.Success, again.Result);
        Assert.Equal(first.ScannedAt, again.ScannedAt);
        Assert.Single(await LogsAsync(db));
        Assert.Single(notifier.Calls);
    }

    [Fact]
    public async Task Scan_WhenTwoGatesScanAtOnce_OnlyOneSucceeds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gate-scan-{Guid.NewGuid():N}.db");
        try
        {
            await using (var setup = CreateFileDb(path))
            {
                await setup.Database.EnsureCreatedAsync();
                await AddTicketAsync(setup, Code, "VALID");
            }

            var notifier = new RecordingNotifier();
            var scans = Enumerable.Range(1, 2).Select(async index =>
            {
                await using var db = CreateFileDb(path);
                return await Service(db, notifier).ScanAsync(Request($"scan-{index}"), CancellationToken.None);
            });

            var responses = await Task.WhenAll(scans);

            Assert.Single(responses, response => response.Result == GateScanResults.Success);
            Assert.Single(responses, response => response.Result == GateScanResults.Duplicate);
            Assert.Single(notifier.Calls);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Scan_WhenSameScanIdArrivesTwiceAtOnce_RecordsOneScan()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gate-scan-{Guid.NewGuid():N}.db");
        try
        {
            await using (var setup = CreateFileDb(path))
            {
                await setup.Database.EnsureCreatedAsync();
                await AddTicketAsync(setup, Code, "VALID");
            }

            var notifier = new RecordingNotifier();
            var scans = Enumerable.Range(1, 2).Select(async _ =>
            {
                await using var db = CreateFileDb(path);
                return await Service(db, notifier).ScanAsync(Request("scan-1"), CancellationToken.None);
            });

            var responses = await Task.WhenAll(scans);

            Assert.All(responses, response => Assert.Equal(GateScanResults.Success, response.Result));
            Assert.Single(notifier.Calls);
            await using var check = CreateFileDb(path);
            Assert.Equal(1, await check.GateAccessLogs.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Scan_WhenWebhookFails_StillSucceedsAndKeepsTicketUsed()
    {
        await using var db = CreateDb();
        await AddTicketAsync(db, Code, "VALID");
        var notifier = new RecordingNotifier(new HttpRequestException("Core is down"));

        var response = await Service(db, notifier).ScanAsync(Request("scan-1"), CancellationToken.None);

        Assert.Equal(GateScanResults.Success, response.Result);
        Assert.Equal("USED", await StatusOfAsync(db, Code));
    }

    [Fact]
    public async Task Scan_WhenWebhookTimesOut_StillSucceeds()
    {
        await using var db = CreateDb();
        await AddTicketAsync(db, Code, "VALID");
        var notifier = new RecordingNotifier(new TaskCanceledException("timeout"));

        var response = await Service(db, notifier).ScanAsync(Request("scan-1"), CancellationToken.None);

        Assert.Equal(GateScanResults.Success, response.Result);
    }

    [Theory]
    [InlineData(null, Code)]
    [InlineData("   ", Code)]
    [InlineData("scan-1", null)]
    [InlineData("scan-1", "   ")]
    public async Task Controller_WhenRequiredFieldIsMissing_Returns400AndWritesNothing(string? scanId, string? ticketCode)
    {
        await using var db = CreateDb();
        await AddTicketAsync(db, Code, "VALID");
        var controller = new GateScanController(Service(db));

        var result = await controller.Scan(
            new GateScanRequest { ScanId = scanId, TicketCode = ticketCode, GateName = "Gate A" },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("VALID", await StatusOfAsync(db, Code));
        Assert.Empty(await LogsAsync(db));
    }

    [Fact]
    public async Task Controller_WhenTicketCodeIsTooLong_Returns400()
    {
        await using var db = CreateDb();
        var controller = new GateScanController(Service(db));

        var result = await controller.Scan(Request("scan-1", new string('A', 51)), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await LogsAsync(db));
    }

    [Fact]
    public async Task Controller_WhenTicketIsRejected_Returns200()
    {
        await using var db = CreateDb();
        await AddTicketAsync(db, Code, "USED");
        var controller = new GateScanController(Service(db));

        var result = await controller.Scan(Request("scan-1"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(GateScanResults.Duplicate, Assert.IsType<GateScanResponse>(ok.Value).Result);
    }

    [Fact]
    public async Task Seeder_OnStartup_KeepsGateLogsAndUsedTicketsOutsideSeed()
    {
        await using var db = CreateDb();
        await SeedHistoryAsync(db);

        await OrganizerDatabaseSeeder.CleanGateHistoryAsync(db, new HashSet<Guid>(), resetOperationalData: false);
        await db.SaveChangesAsync();

        Assert.Single(await LogsAsync(db));
        Assert.Equal("USED", await StatusOfAsync(db, "USED-TICKET"));
        Assert.Equal("VALID", await StatusOfAsync(db, "VALID-TICKET"));
        Assert.Null(await StatusOfAsync(db, "OLD-TICKET"));
    }

    [Fact]
    public async Task Seeder_OnResetAll_ClearsGateLogsAndUsedTicketsOutsideSeed()
    {
        await using var db = CreateDb();
        await SeedHistoryAsync(db);

        await OrganizerDatabaseSeeder.CleanGateHistoryAsync(db, new HashSet<Guid>(), resetOperationalData: true);
        await db.SaveChangesAsync();

        Assert.Empty(await LogsAsync(db));
        Assert.Null(await StatusOfAsync(db, "USED-TICKET"));
        Assert.Equal("VALID", await StatusOfAsync(db, "VALID-TICKET"));
    }

    private static async Task SeedHistoryAsync(OrganizerDbContext db)
    {
        await AddTicketAsync(db, "USED-TICKET", "USED");
        await AddTicketAsync(db, "VALID-TICKET", "VALID");
        await AddTicketAsync(db, "OLD-TICKET", "TRANSFERRED");
        db.GateAccessLogs.Add(new GateAccessLog { TicketCode = "USED-TICKET", GateName = "Gate A" });
        await db.SaveChangesAsync();
    }

    private static GateScanService Service(OrganizerDbContext db, ITicketUsedNotifier? notifier = null)
        => new(db, notifier ?? new RecordingNotifier(), NullLogger<GateScanService>.Instance);

    private static GateScanRequest Request(string scanId, string ticketCode = Code, string gateName = "Gate A")
        => new() { ScanId = scanId, TicketCode = ticketCode, GateName = gateName };

    private static async Task AddTicketAsync(OrganizerDbContext db, string code, string status)
    {
        db.MockTickets.Add(new MockTicket
        {
            TicketCode = code,
            EventName = "Concert",
            SeatZone = "Zone A",
            OwnerEmail = "owner@example.com",
            Status = status
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string?> StatusOfAsync(OrganizerDbContext db, string code)
        => await db.MockTickets.AsNoTracking()
            .Where(ticket => ticket.TicketCode == code)
            .Select(ticket => ticket.Status)
            .FirstOrDefaultAsync();

    private static Task<List<GateAccessLog>> LogsAsync(OrganizerDbContext db)
        => db.GateAccessLogs.AsNoTracking().ToListAsync();

    private static OrganizerDbContext CreateDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<OrganizerDbContext>().UseSqlite(connection).Options;
        var db = new OrganizerDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static OrganizerDbContext CreateFileDb(string path)
    {
        var options = new DbContextOptionsBuilder<OrganizerDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        return new OrganizerDbContext(options);
    }
}
