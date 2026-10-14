using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MockOrganizer.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMockOrderTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ticket_tiers",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "mock_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "text", nullable: false),
                    tier_name = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    buyer_email = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mock_orders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mock_orders_tier_id",
                table: "mock_orders",
                column: "tier_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mock_orders");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ticket_tiers");
        }
    }
}
