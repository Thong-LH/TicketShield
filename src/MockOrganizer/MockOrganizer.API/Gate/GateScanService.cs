using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;

namespace MockOrganizer.API.Gate;

public sealed class GateScanService(
    OrganizerDbContext db,
    ITicketUsedNotifier notifier,
    ILogger<GateScanService> logger)
{
    private const int MaxTicketCodeLength = 50;
    private const int MaxLabelLength = 100;
    private const string DefaultGateName = "Main Gate";

    public static bool IsValid(GateScanRequest request)
    {
        var ticketCode = request.TicketCode?.Trim();
        return !string.IsNullOrWhiteSpace(request.ScanId)
            && !string.IsNullOrEmpty(ticketCode)
            && ticketCode.Length <= MaxTicketCodeLength
            && (request.GateName?.Trim().Length ?? 0) <= MaxLabelLength
            && (request.ScannerDeviceId?.Trim().Length ?? 0) <= MaxLabelLength;
    }

    public async Task<GateScanResponse> ScanAsync(GateScanRequest request, CancellationToken cancellationToken)
    {
        var scanId = request.ScanId!.Trim();
        var ticketCode = request.TicketCode!.Trim();

        var previous = await FindByScanIdAsync(scanId, cancellationToken);
        if (previous != null)
        {
            return await BuildResponseAsync(previous, cancellationToken);
        }

        GateAccessLog log;
        try
        {
            log = await RecordScanAsync(request, scanId, ticketCode, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Cùng scanId đến cùng lúc: bản kia đã ghi trước thì trả kết quả của nó.
            db.ChangeTracker.Clear();
            previous = await FindByScanIdAsync(scanId, cancellationToken);
            if (previous == null)
            {
                throw;
            }

            return await BuildResponseAsync(previous, cancellationToken);
        }

        if (log.ScanResult == GateScanResults.Success)
        {
            await NotifyCoreAsync(ticketCode);
        }

        return await BuildResponseAsync(log, cancellationToken);
    }

    private async Task<GateAccessLog> RecordScanAsync(
        GateScanRequest request,
        string scanId,
        string ticketCode,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Làm tròn tới mili giây để giờ trả về trùng với giờ đọc lại từ database.
        var now = DateTimeOffset.UtcNow;
        var scannedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        var log = new GateAccessLog
        {
            ScanId = scanId,
            TicketCode = ticketCode,
            GateName = string.IsNullOrWhiteSpace(request.GateName) ? DefaultGateName : request.GateName.Trim(),
            ScannerDeviceId = request.ScannerDeviceId?.Trim(),
            ScannedAt = scannedAt,
            ScanResult = await ClaimTicketAsync(ticketCode, scannedAt, cancellationToken)
        };

        db.GateAccessLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return log;
    }

    // Đổi trạng thái trước rồi mới đọc, nên hai lần quét cùng lúc chỉ một lần đổi được.
    private async Task<string> ClaimTicketAsync(
        string ticketCode,
        DateTimeOffset scannedAt,
        CancellationToken cancellationToken)
    {
        var claimed = await db.MockTickets
            .Where(ticket => ticket.TicketCode == ticketCode && ticket.Status == "VALID")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(ticket => ticket.Status, "USED")
                    .SetProperty(ticket => ticket.UpdatedAt, scannedAt),
                cancellationToken);

        if (claimed == 1)
        {
            return GateScanResults.Success;
        }

        var status = await db.MockTickets.AsNoTracking()
            .Where(ticket => ticket.TicketCode == ticketCode)
            .Select(ticket => ticket.Status)
            .FirstOrDefaultAsync(cancellationToken);

        return status switch
        {
            null => GateScanResults.Invalid,
            "USED" => GateScanResults.Duplicate,
            _ => GateScanResults.Revoked
        };
    }

    // Quyết định cho vào cổng không phụ thuộc Core. Lỗi ở đây chỉ làm Core thiếu một dòng USED.
    private async Task NotifyCoreAsync(string ticketCode)
    {
        try
        {
            await notifier.NotifyAsync(ticketCode, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not notify Core that ticket {TicketCode} was used.", ticketCode);
        }
    }

    private Task<GateAccessLog?> FindByScanIdAsync(string scanId, CancellationToken cancellationToken)
        => db.GateAccessLogs.AsNoTracking().FirstOrDefaultAsync(log => log.ScanId == scanId, cancellationToken);

    private async Task<GateScanResponse> BuildResponseAsync(GateAccessLog log, CancellationToken cancellationToken)
    {
        var ticket = await db.MockTickets.AsNoTracking()
            .FirstOrDefaultAsync(row => row.TicketCode == log.TicketCode, cancellationToken);

        var firstScan = log.ScanResult == GateScanResults.Duplicate
            ? await FindFirstSuccessAsync(log.TicketCode, cancellationToken)
            : null;

        return new GateScanResponse
        {
            ScanId = log.ScanId ?? string.Empty,
            Result = log.ScanResult,
            TicketCode = log.TicketCode,
            EventName = ticket?.EventName,
            SeatZone = ticket?.SeatZone,
            ScannedAt = log.ScannedAt,
            FirstScannedAt = firstScan?.ScannedAt,
            FirstGateName = firstScan?.GateName
        };
    }

    private async Task<GateAccessLog?> FindFirstSuccessAsync(string ticketCode, CancellationToken cancellationToken)
    {
        var scans = await db.GateAccessLogs.AsNoTracking()
            .Where(log => log.TicketCode == ticketCode && log.ScanResult == GateScanResults.Success)
            .ToListAsync(cancellationToken);

        return scans.MinBy(log => log.ScannedAt);
    }
}
