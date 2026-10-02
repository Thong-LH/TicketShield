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
