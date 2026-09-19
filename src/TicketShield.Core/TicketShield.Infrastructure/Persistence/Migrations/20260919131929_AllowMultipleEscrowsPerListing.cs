using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleEscrowsPerListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_escrow_transactions_listing_id",
                table: "escrow_transactions");

            migrationBuilder.CreateIndex(
                name: "ix_escrow_transactions_listing_id",
                table: "escrow_transactions",
                column: "listing_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_escrow_transactions_listing_id",
                table: "escrow_transactions");

            migrationBuilder.CreateIndex(
                name: "ix_escrow_transactions_listing_id",
                table: "escrow_transactions",
                column: "listing_id",
                unique: true);
        }
    }
}
