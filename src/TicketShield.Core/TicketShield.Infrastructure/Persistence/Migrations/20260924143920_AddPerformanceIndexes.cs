using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_resale_listings_is_private_listing_status_created_at",
                table: "resale_listings",
                columns: new[] { "is_private", "listing_status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_escrow_transactions_payment_reference",
                table: "escrow_transactions",
                column: "payment_reference");

            migrationBuilder.CreateIndex(
                name: "ix_escrow_transactions_status_unlock_at",
                table: "escrow_transactions",
                columns: new[] { "status", "unlock_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_resale_listings_is_private_listing_status_created_at",
                table: "resale_listings");

            migrationBuilder.DropIndex(
                name: "ix_escrow_transactions_payment_reference",
                table: "escrow_transactions");

            migrationBuilder.DropIndex(
                name: "ix_escrow_transactions_status_unlock_at",
                table: "escrow_transactions");
        }
    }
}
