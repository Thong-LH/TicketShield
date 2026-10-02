using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEscrowSettlementSweepIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_escrows_settlement_sweep",
                table: "escrow_transactions",
                columns: new[] { "status", "in_settlement_buffer", "unlock_at" },
                filter: "status = 'Locked' AND in_settlement_buffer = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_escrows_settlement_sweep",
                table: "escrow_transactions");
        }
    }
}
