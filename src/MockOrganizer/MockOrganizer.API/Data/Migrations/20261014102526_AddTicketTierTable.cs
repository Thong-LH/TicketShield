using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MockOrganizer.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketTierTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ticket_tiers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "text", nullable: false),
                    tier_name = table.Column<string>(type: "text", nullable: false),
                    price = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    total_quantity = table.Column<int>(type: "integer", nullable: false),
                    available_quantity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_tiers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_tiers_event_id",
                table: "ticket_tiers",
                column: "event_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_tiers");
        }
    }
}
