using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDisputeHarvestColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "transferred_at",
                table: "escrow_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gate_log_snapshot",
                table: "disputes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "harvested_at",
                table: "disputes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recommendation",
                table: "disputes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "transferred_at",
                table: "escrow_transactions");

            migrationBuilder.DropColumn(
                name: "gate_log_snapshot",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "harvested_at",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "recommendation",
                table: "disputes");
        }
    }
}
