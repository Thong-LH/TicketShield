using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Disputes;
using TicketShield.Domain.Entities;

namespace TicketShield.Infrastructure.Workers;

public class DisputeHarvestWorker : BackgroundService
{
    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DisputeHarvestWorker> _logger;
    private readonly TimeSpan _interval;

    public DisputeHarvestWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<DisputeHarvestWorker> logger,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(15);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await HarvestPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Dispute harvest failed.");
            }
        }
    }

    public async Task<int> HarvestPendingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ITicketShieldDbContext>();
        var lockKey = ComputeWorkerLockKey(nameof(DisputeHarvestWorker));
        await using var lockTx = await db.TryBeginAdvisoryLockTransactionAsync(lockKey, cancellationToken);
        if (lockTx == null)
        {
            _logger.LogDebug("Another instance is already harvesting dispute evidence. Skipping cycle.");
            return 0;
        }

        var client = scope.ServiceProvider.GetRequiredService<IGateAccessLogClient>();
        var pending = await db.OutboxMessages
            .Where(row => row.EventType == nameof(DisputeHarvestRequested) && row.ProcessedAt == null)
            .ToListAsync(cancellationToken);

        var finished = 0;
        foreach (var message in pending)
        {
            if (await TryHarvestAsync(db, client, message, cancellationToken))
            {
                finished++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return finished;
    }

    private async Task<bool> TryHarvestAsync(
        ITicketShieldDbContext db,
        IGateAccessLogClient client,
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<DisputeHarvestRequested>(message.Payload);
        if (request == null || request.DisputeId == Guid.Empty)
        {
            MarkProcessed(message);
            return true;
        }

        var dispute = await db.Disputes
            .Include(row => row.Escrow)
            .FirstOrDefaultAsync(row => row.Id == request.DisputeId, cancellationToken);
        if (dispute == null)
        {
            MarkProcessed(message);
            return true;
        }

        if (dispute.HarvestedAt.HasValue)
        {
            MarkProcessed(message);
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var codes = SplitCodes(dispute.Escrow.NewTicketCode);
        if (codes.Count == 0 || dispute.Escrow.TransferredAt is null)
        {
            dispute.Recommendation = null;
            dispute.HarvestedAt = now;
            dispute.UpdatedAt = now;
            MarkProcessed(message);
            return true;
        }

        var logs = new List<TicketGateLog>(codes.Count);
        try
        {
            foreach (var code in codes)
            {
                var scans = await client.GetScansAsync(code, cancellationToken);
                logs.Add(new TicketGateLog
                {
                    TicketCode = code,
                    Scans = scans.Select(scan => CopyScan(code, scan)).ToArray()
                });
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Gate log query failed for dispute {DisputeId}.", dispute.Id);
            return false;
        }

        dispute.Recommendation = DisputeRecommendation.Decide(dispute.Escrow.TransferredAt, logs);
        dispute.GateLogSnapshot = JsonSerializer.Serialize(logs.SelectMany(log => log.Scans).ToArray(), SnapshotJson);
        dispute.HarvestedAt = now;
        dispute.UpdatedAt = now;
        MarkProcessed(message);
        return true;
    }

    private static GateScan CopyScan(string ticketCode, GateScan scan) =>
        new()
        {
            TicketCode = string.IsNullOrWhiteSpace(scan.TicketCode) ? ticketCode : scan.TicketCode,
            ScannedAt = scan.ScannedAt,
            ScanResult = scan.ScanResult,
            GateName = scan.GateName,
            ScannerDeviceId = scan.ScannerDeviceId
        };

    private static List<string> SplitCodes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(code => code.Length > 0)
            .ToList();
    }

    private static void MarkProcessed(OutboxMessage message)
    {
        var now = DateTimeOffset.UtcNow;
        message.ProcessedAt = now;
        message.UpdatedAt = now;
    }

    private static long ComputeWorkerLockKey(string workerName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ts:worker:" + workerName));
        return BitConverter.ToInt64(hash, 0);
    }
}
