using Microsoft.EntityFrameworkCore;
using TicketShield.Settlement.API.Data;
using TicketShield.Settlement.API.Gateway;

namespace TicketShield.Settlement.API.Payouts;

public class PayoutCommand
{
    public Guid EscrowId { get; set; }
    public Guid SellerId { get; set; }
    public decimal Amount { get; set; }
    public int RetryCount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
}

public class AcceptResult
{
    public bool Accepted { get; set; }
    public string? Error { get; set; }
    public TransferState State { get; set; }
    public string? BankReference { get; set; }
    public bool GatewayCalled { get; set; }
}

public interface ICorePayoutReporter
{
    Task<bool> ReportAsync(Guid escrowId, string idempotencyKey, bool succeeded, string? bankReference, CancellationToken cancellationToken);
}

public class SettlementPayoutService
{
    public const int MaxAttempts = 3;

    private readonly SettlementDbContext _db;
    private readonly ISettlementGateway _gateway;
    private readonly ICorePayoutReporter _reporter;
    private readonly TimeSpan _firstWait;

    public SettlementPayoutService(
        SettlementDbContext db,
        ISettlementGateway gateway,
        ICorePayoutReporter reporter,
        TimeSpan? firstWait = null)
    {
        _db = db;
        _gateway = gateway;
        _reporter = reporter;
        _firstWait = firstWait ?? TimeSpan.FromSeconds(1);
    }

    public static TimeSpan WaitAfterAttempt(int completedAttempts, TimeSpan first)
        => first * (1 << Math.Max(0, completedAttempts - 1));

    public async Task<AcceptResult> AcceptAsync(PayoutCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.AccountNumber) || command.Amount <= 0 || string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return new AcceptResult { Accepted = false, Error = "Lệnh thiếu số tài khoản hoặc số tiền." };
        }

        var existing = await _db.TransferOrders.SingleOrDefaultAsync(row => row.IdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (existing?.BankReference != null)
        {
            return new AcceptResult
            {
                Accepted = true,
                State = TransferState.Succeeded,
                BankReference = existing.BankReference
            };
        }

        if (existing?.State == TransferState.Failed)
        {
            return new AcceptResult { Accepted = true, State = TransferState.Failed };
        }

        var order = existing ?? new TransferOrder
        {
            IdempotencyKey = command.IdempotencyKey,
            EscrowId = command.EscrowId,
            SellerId = command.SellerId,
            RetryNumber = command.RetryCount,
            Amount = command.Amount,
            BankCode = command.BankCode,
            AccountNumber = command.AccountNumber.Trim(),
            AccountName = command.AccountName
        };
        if (existing == null)
        {
            _db.TransferOrders.Add(order);
        }

        if (order.State is TransferState.Exhausted)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new AcceptResult { Accepted = true, State = order.State };
        }

        var called = await CallGatewayAsync(order, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await DeliverReportIfReadyAsync(order, cancellationToken);
        return new AcceptResult
        {
            Accepted = true,
            State = order.State,
            BankReference = order.BankReference,
            GatewayCalled = called
        };
    }

    public async Task<int> RetryDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var due = await _db.TransferOrders
            .Where(row => row.State == TransferState.Pending && row.NextAttemptAt != null && row.NextAttemptAt <= now)
            .ToListAsync(cancellationToken);
        foreach (var order in due)
        {
            await CallGatewayAsync(order, cancellationToken);
            await DeliverReportIfReadyAsync(order, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    public async Task<int> RedeliverReportsAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _db.TransferOrders
            .Where(row => row.ReportDeliveredAt == null && (row.State == TransferState.Succeeded || row.State == TransferState.Failed))
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var order in pending)
        {
            if (await DeliverReportIfReadyAsync(order, cancellationToken))
            {
                sent++;
            }
        }

        return sent;
    }

    public async Task<TransferOrder?> FindAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => await _db.TransferOrders.AsNoTracking().SingleOrDefaultAsync(row => row.IdempotencyKey == idempotencyKey, cancellationToken);

    private async Task<bool> CallGatewayAsync(TransferOrder order, CancellationToken cancellationToken)
    {
        if (order.AttemptCount >= MaxAttempts || order.State is TransferState.Succeeded or TransferState.Failed or TransferState.Exhausted)
        {
            return false;
        }

        var transfer = await _gateway.TransferAsync(new GatewayTransferRequest
        {
            EscrowId = order.EscrowId,
            RetryCount = order.RetryNumber,
            AccountNumber = order.AccountNumber,
            Amount = order.Amount
        }, cancellationToken);

        order.AttemptCount++;
        order.LastAttemptAt = DateTimeOffset.UtcNow;
        order.UpdatedAt = order.LastAttemptAt.Value;
        if (transfer.Outcome == GatewayOutcome.Succeeded)
        {
            order.State = TransferState.Succeeded;
            order.BankReference = transfer.BankReferenceCode;
            order.NextAttemptAt = null;
            order.LastError = null;
            return true;
        }

        if (transfer.Outcome == GatewayOutcome.InvalidAccount)
        {
            order.State = TransferState.Failed;
            order.LastError = "STK không hợp lệ";
            order.NextAttemptAt = null;
            return true;
        }

        order.LastError = "Ngân hàng timeout";
        if (order.AttemptCount >= MaxAttempts)
        {
            order.State = TransferState.Exhausted;
            order.NextAttemptAt = null;
            return true;
        }

        order.State = TransferState.Pending;
        order.NextAttemptAt = order.LastAttemptAt + WaitAfterAttempt(order.AttemptCount, _firstWait);
        return true;
    }

    private async Task<bool> DeliverReportIfReadyAsync(TransferOrder order, CancellationToken cancellationToken)
    {
        if (order.ReportDeliveredAt != null || order.State is not (TransferState.Succeeded or TransferState.Failed or TransferState.Exhausted))
        {
            return false;
        }

        var delivered = await _reporter.ReportAsync(
            order.EscrowId,
            order.IdempotencyKey,
            order.State == TransferState.Succeeded,
            order.BankReference,
            cancellationToken);
        if (!delivered)
        {
            return false;
        }

        order.ReportDeliveredAt = DateTimeOffset.UtcNow;
        order.UpdatedAt = order.ReportDeliveredAt.Value;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
