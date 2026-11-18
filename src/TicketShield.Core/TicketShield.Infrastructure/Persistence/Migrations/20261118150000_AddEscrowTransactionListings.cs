using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEscrowTransactionListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "escrow_transaction_listings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    escrow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_escrow_transaction_listings", x => x.id);
                    table.ForeignKey(
                        name: "fk_escrow_transaction_listings_escrow_transactions_escrow_id",
                        column: x => x.escrow_id,
                        principalTable: "escrow_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_escrow_transaction_listings_resale_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "resale_listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_escrow_transaction_listings_escrow_id_listing_id",
                table: "escrow_transaction_listings",
                columns: new[] { "escrow_id", "listing_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_escrow_transaction_listings_listing_id",
                table: "escrow_transaction_listings",
                column: "listing_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escrow_transaction_listings");
        }
    }
}
