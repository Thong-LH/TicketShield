using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TicketShield.Domain.Entities;
using TicketShield.Infrastructure.Persistence;

namespace TicketShield.UnitTests.Persistence;

public class PayoutRecordSchemaTests
{
    [Fact]
    public void Settlement_schema_stores_seller_on_payout_and_retry_count_on_escrow()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseNpgsql("Host=localhost;Database=ticketshield_schema_test")
            .Options;

        using var db = new TicketShieldDbContext(options);

        var escrow = db.Model.FindEntityType(typeof(EscrowTransaction));
        Assert.NotNull(escrow);
        Assert.Equal("escrow_transactions", escrow!.GetTableName());
        Assert.Equal("retry_count", ColumnName(escrow, nameof(EscrowTransaction.RetryCount)));

        var payout = db.Model.FindEntityType(typeof(PayoutTransaction));
        Assert.NotNull(payout);
        Assert.Equal("payout_transactions", payout!.GetTableName());
        Assert.Equal("seller_id", ColumnName(payout, nameof(PayoutTransaction.SellerId)));
        Assert.Equal("recipient_bank_code", ColumnName(payout, nameof(PayoutTransaction.RecipientBankCode)));
        Assert.Equal("recipient_account_number", ColumnName(payout, nameof(PayoutTransaction.RecipientAccountNumber)));
        Assert.Equal("amount", ColumnName(payout, nameof(PayoutTransaction.Amount)));
        Assert.Equal("status", ColumnName(payout, nameof(PayoutTransaction.Status)));
    }

    [Fact]
    public void Escrow_keeps_hold_index_and_adds_locked_settlement_sweep_index()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseNpgsql("Host=localhost;Database=ticketshield_schema_test")
            .Options;

        using var db = new TicketShieldDbContext(options);
        var escrow = db.Model.FindEntityType(typeof(EscrowTransaction));
        Assert.NotNull(escrow);

        var holdIndex = escrow!.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(EscrowTransaction.Status),
                nameof(EscrowTransaction.UnlockAt)
            ]));
        Assert.Null(holdIndex.GetFilter());

        var sweepIndex = escrow.GetIndexes().Single(index =>
            index.GetDatabaseName() == "idx_escrows_settlement_sweep");
        Assert.Equal(
            [
                nameof(EscrowTransaction.Status),
                nameof(EscrowTransaction.InSettlementBuffer),
                nameof(EscrowTransaction.UnlockAt)
            ],
            sweepIndex.Properties.Select(property => property.Name).ToArray());
        Assert.Equal(
            "status = 'Locked' AND in_settlement_buffer = true",
            sweepIndex.GetFilter());
    }

    private static string ColumnName(IEntityType entity, string propertyName)
    {
        var property = entity.FindProperty(propertyName);
        Assert.NotNull(property);
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var columnName = property!.GetColumnName(table);
        Assert.False(string.IsNullOrEmpty(columnName));
        return columnName!;
    }
}
