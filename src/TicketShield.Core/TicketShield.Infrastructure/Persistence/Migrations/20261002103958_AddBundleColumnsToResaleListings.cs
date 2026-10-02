using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBundleColumnsToResaleListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bundle_id",
                table: "resale_listings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bundle_total_tickets",
                table: "resale_listings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_bundle_all_or_nothing",
                table: "resale_listings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "idx_resale_records_bundle_id",
                table: "resale_listings",
                column: "bundle_id",
                filter: "bundle_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_resale_records_bundle_id",
                table: "resale_listings");

            migrationBuilder.DropColumn(
                name: "bundle_id",
                table: "resale_listings");

            migrationBuilder.DropColumn(
                name: "bundle_total_tickets",
                table: "resale_listings");

            migrationBuilder.DropColumn(
                name: "is_bundle_all_or_nothing",
                table: "resale_listings");
        }
    }
}
