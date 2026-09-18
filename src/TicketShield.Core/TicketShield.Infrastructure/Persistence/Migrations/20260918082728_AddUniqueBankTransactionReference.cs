using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueBankTransactionReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_escrow_transactions_bank_transaction_reference",
                table: "escrow_transactions",
                column: "bank_transaction_reference",
                unique: true,
                filter: "bank_transaction_reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_escrow_transactions_bank_transaction_reference",
                table: "escrow_transactions");
        }
    }
}
