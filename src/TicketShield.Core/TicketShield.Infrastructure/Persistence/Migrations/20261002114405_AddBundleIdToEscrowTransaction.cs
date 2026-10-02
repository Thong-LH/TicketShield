using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBundleIdToEscrowTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bundle_id",
                table: "escrow_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_escrows_bundle_id",
                table: "escrow_transactions",
                column: "bundle_id",
                filter: "bundle_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_escrows_bundle_id",
                table: "escrow_transactions");

            migrationBuilder.DropColumn(
                name: "bundle_id",
                table: "escrow_transactions");
        }
    }
}
