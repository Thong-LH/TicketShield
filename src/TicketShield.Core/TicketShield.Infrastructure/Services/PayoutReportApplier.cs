using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Services;

public class PayoutReportApplier : IPayoutReportApplier
{
    private readonly ITicketShieldDbContext _db;
    private readonly IPayoutRealtimeNotifier? _payoutNotifier;
    private readonly IEmailService? _emailService;
    private readonly IEmailTemplateService? _emailTemplates;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PayoutReportApplier> _logger;

    public PayoutReportApplier(
        ITicketShieldDbContext db,
        IPayoutRealtimeNotifier? payoutNotifier = null,
        IEmailService? emailService = null,
        IEmailTemplateService? emailTemplates = null,
        IConfiguration? configuration = null,
        ILogger<PayoutReportApplier>? logger = null)
    {
        _db = db;
        _payoutNotifier = payoutNotifier;
        _emailService = emailService;
        _emailTemplates = emailTemplates;
        _configuration = configuration ?? new ConfigurationBuilder().Build();
        _logger = logger ?? new LoggerFactory().CreateLogger<PayoutReportApplier>();
    }

    public async Task<bool> ApplyAsync(Guid escrowId, bool succeeded, string? bankReference, CancellationToken cancellationToken = default)
    {
        var escrow = await _db.EscrowTransactions
            .Include(row => row.Seller)
            .Include(row => row.Listing)
                .ThenInclude(l => l.Event)
            .Include(row => row.Listing)
                .ThenInclude(l => l.Tier)
            .SingleOrDefaultAsync(row => row.Id == escrowId, cancellationToken);

        if (escrow == null || escrow.Status != EscrowStatus.Releasing)
        {
            return false;
        }

        var payout = await _db.PayoutTransactions.FirstOrDefaultAsync(row => row.EscrowId == escrowId, cancellationToken);
        if (payout == null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (succeeded)
        {
            if (!string.IsNullOrWhiteSpace(payout.BankReferenceCode))
            {
                return false;
            }

            payout.Status = PayoutStatus.Success;
            payout.BankReferenceCode = bankReference;
            payout.ProcessedAt = now;
            payout.UpdatedAt = now;
            escrow.Status = EscrowStatus.Released;
            escrow.InSettlementBuffer = false;
            escrow.UpdatedAt = now;
            await _db.SaveChangesAsync(cancellationToken);

            if (_payoutNotifier != null)
            {
                var payload = new
                {
                    EscrowId = escrow.Id,
                    ListingId = escrow.ListingId,
                    SellerId = escrow.SellerId,
                    Amount = payout.Amount,
                    NetSellerPayout = escrow.NetSellerPayout,
                    BankCode = payout.RecipientBankCode,
                    AccountNumber = payout.RecipientAccountNumber,
                    AccountName = payout.RecipientAccountName,
                    BankReference = bankReference,
                    Status = "Success",
                    ProcessedAt = now
                };
                await _payoutNotifier.NotifyPayoutCompletedAsync(escrow.SellerId, escrow.Id, escrow.ListingId, payload, cancellationToken);
            }

            if (_emailService != null && _emailTemplates != null)
            {
                await TrySendPayoutStatementEmailAsync(escrow, payout, bankReference, now, cancellationToken);
            }

            return true;
        }

        if (payout.Status == PayoutStatus.Failed)
        {
            return false;
        }

        payout.Status = PayoutStatus.Failed;
        payout.LastErrorMessage = "STK không hợp lệ";
        payout.UpdatedAt = now;
        escrow.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task TrySendPayoutStatementEmailAsync(
        EscrowTransaction escrow,
        PayoutTransaction payout,
        string? bankReference,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var sellerEmail = escrow.Seller?.Email;
            var sellerName = escrow.Seller?.FullName;

            if (string.IsNullOrWhiteSpace(sellerEmail))
            {
                var synced = await TrySyncSellerEmailFromIdentityAsync(escrow.SellerId, cancellationToken);
                if (synced != null)
                {
                    sellerEmail = synced.Value.Email;
                    sellerName = string.IsNullOrWhiteSpace(sellerName) ? synced.Value.FullName : sellerName;
                }
            }

            if (string.IsNullOrWhiteSpace(sellerEmail))
            {
                _logger.LogWarning("Cannot send payout statement email: seller email not found for SellerId {SellerId}", escrow.SellerId);
                return;
            }

            var grossPrice = escrow.Listing?.ResalePrice ?? (escrow.NetSellerPayout + escrow.SellerFee);
            if (grossPrice <= 0) grossPrice = escrow.NetSellerPayout;
            var sellerFee = escrow.SellerFee > 0 ? escrow.SellerFee : Math.Max(grossPrice - escrow.NetSellerPayout, 0);
            var eventName = escrow.Listing?.Event?.Name ?? "Vé Sự Kiện TicketShield";
            var tierName = escrow.Listing?.Tier?.TierName ?? "Hạng vé tiêu chuẩn";
            var ticketCode = escrow.Listing?.OriginalTicketCode ?? escrow.NewTicketCode ?? "N/A";
            var displayName = !string.IsNullOrWhiteSpace(sellerName)
                ? sellerName
                : (payout.RecipientAccountName ?? "Quý khách");

            var html = _emailTemplates!.GetSellerPayoutStatementEmailHtml(
                sellerName: displayName,
                payoutCode: payout.PayoutCode,
                bankReference: bankReference ?? payout.BankReferenceCode ?? "N/A",
                escrowCode: escrow.PaymentReference ?? $"ESC-{escrow.Id.ToString("N")[..8].ToUpperInvariant()}",
                eventName: eventName,
                tierName: tierName,
                ticketCode: ticketCode,
                grossPrice: grossPrice,
                sellerFee: sellerFee,
                netPayout: escrow.NetSellerPayout,
                bankCode: payout.RecipientBankCode ?? "NAPAS 247",
                accountNumber: payout.RecipientAccountNumber ?? "N/A",
                accountName: payout.RecipientAccountName ?? displayName,
                processedAt: processedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm:ss")
            );

            await _emailService!.SendEmailAsync(
                sellerEmail,
                $"[TicketShield] Sao kê giải ngân bán vé #{payout.PayoutCode} - Thành công",
                html,
                cancellationToken);

            _logger.LogInformation("Payout statement email successfully sent to {Email} for PayoutCode {PayoutCode}", sellerEmail, payout.PayoutCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send payout statement email for EscrowId {EscrowId}", escrow.Id);
        }
    }

    private async Task<(string Email, string FullName)?> TrySyncSellerEmailFromIdentityAsync(Guid sellerId, CancellationToken cancellationToken)
    {
        try
        {
            var identityConnStr = _configuration.GetConnectionString("IdentityConnection")
                ?? "Host=localhost;Port=5432;Database=identity_db;Username=postgres;Password=12345";
            await using var conn = new Npgsql.NpgsqlConnection(identityConnStr);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new Npgsql.NpgsqlCommand(
                "SELECT \"Email\", \"FullName\" FROM users WHERE \"Id\" = @userId LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("userId", sellerId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var email = reader.GetString(0);
                var fullName = reader.GetString(1);

                await _db.ShadowUsers
                    .Where(u => u.Id == sellerId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.Email, email)
                        .SetProperty(u => u.FullName, fullName)
                        .SetProperty(u => u.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);

                return (email, fullName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync seller email from identity_db for SellerId {SellerId}", sellerId);
        }

        return null;
    }
}
