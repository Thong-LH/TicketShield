using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Services;

public class EscrowPayoutSettler : IEscrowPayoutSettler
{
    private readonly ITicketShieldDbContext _db;
    private readonly IEscrowSettlementCas _cas;
    private readonly IConfiguration _configuration;

    public EscrowPayoutSettler(ITicketShieldDbContext db, IEscrowSettlementCas cas, IConfiguration? configuration = null)
    {
        _db = db;
        _cas = cas;
        _configuration = configuration ?? new ConfigurationBuilder().Build();
    }

    public async Task<bool> TrySettleAsync(Guid escrowId, CancellationToken cancellationToken = default)
    {
        var escrow = await _db.EscrowTransactions
            .AsNoTracking()
            .Where(row => row.Id == escrowId)
            .Select(row => new { row.SellerId, row.Seller.PayoutAccountNumber })
            .SingleOrDefaultAsync(cancellationToken);

        if (escrow == null)
        {
            return false;
        }

        var sellerAccount = escrow.PayoutAccountNumber;
        if (!PayoutAccountRules.IsPresent(sellerAccount))
        {
            sellerAccount = await TrySyncSellerBankFromIdentityAsync(escrow.SellerId, cancellationToken);
            if (!PayoutAccountRules.IsPresent(sellerAccount))
            {
                return false;
            }
        }

        return await _cas.TryBeginReleaseAsync(escrowId, cancellationToken);
    }

    private async Task<string?> TrySyncSellerBankFromIdentityAsync(Guid sellerId, CancellationToken cancellationToken)
    {
        try
        {
            var identityConnStr = _configuration.GetConnectionString("IdentityConnection")
                ?? "Host=localhost;Port=5432;Database=identity_db;Username=postgres;Password=12345";
            await using var conn = new Npgsql.NpgsqlConnection(identityConnStr);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new Npgsql.NpgsqlCommand(
                "SELECT \"BankCode\", \"BankAccountNumber\", \"AccountHolderName\" FROM user_bank_accounts WHERE \"UserId\" = @userId ORDER BY \"IsDefault\" DESC, \"CreatedAt\" DESC LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("userId", sellerId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var bankCode = reader.GetString(0);
                var accNumber = reader.GetString(1);
                var accName = reader.GetString(2);

                await _db.ShadowUsers
                    .Where(u => u.Id == sellerId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.PayoutBankCode, bankCode)
                        .SetProperty(u => u.PayoutAccountNumber, accNumber)
                        .SetProperty(u => u.PayoutAccountName, accName)
                        .SetProperty(u => u.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);

                return accNumber;
            }
        }
        catch
        {
            // Fallback fails gracefully if identity_db is not reachable directly
        }

        return null;
    }

    public async Task<bool> TryResumeReleaseAsync(Guid escrowId, CancellationToken cancellationToken = default)
    {
        // FIX Lỗi 02: Khôi phục và tái kích hoạt giải ngân cho escrow bị treo
        var escrow = await _db.EscrowTransactions
            .Include(e => e.PayoutTransaction)
            .FirstOrDefaultAsync(row => row.Id == escrowId, cancellationToken);

        if (escrow == null)
        {
            return false;
        }

        if (escrow.Status != EscrowStatus.Locked && escrow.Status != EscrowStatus.Releasing)
        {
            return false;
        }

        var sellerAccount = await _db.ShadowUsers
            .Where(u => u.Id == escrow.SellerId)
            .Select(u => u.PayoutAccountNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (!PayoutAccountRules.IsPresent(sellerAccount))
        {
            sellerAccount = await TrySyncSellerBankFromIdentityAsync(escrow.SellerId, cancellationToken);
            if (!PayoutAccountRules.IsPresent(sellerAccount))
            {
                return false;
            }
        }

        if (escrow.PayoutTransaction != null && escrow.PayoutTransaction.Status == PayoutStatus.Failed)
        {
            escrow.PayoutTransaction.Status = PayoutStatus.Pending;
            escrow.PayoutTransaction.LastErrorMessage = null;
            escrow.PayoutTransaction.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await _cas.TryBeginReleaseAsync(escrowId, cancellationToken);
    }
}
