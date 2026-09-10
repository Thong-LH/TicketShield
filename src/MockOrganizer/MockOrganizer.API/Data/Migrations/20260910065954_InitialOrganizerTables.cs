using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MockOrganizer.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrganizerTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gate_access_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_code = table.Column<string>(type: "text", nullable: false),
                    gate_name = table.Column<string>(type: "text", nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scan_result = table.Column<string>(type: "text", nullable: false),
                    scanner_device_id = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gate_access_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mock_otps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_code = table.Column<string>(type: "text", nullable: false),
                    owner_email = table.Column<string>(type: "text", nullable: false),
                    otp_code = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_used = table.Column<bool>(type: "boolean", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mock_otps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mock_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_code = table.Column<string>(type: "text", nullable: false),
                    event_name = table.Column<string>(type: "text", nullable: false),
                    seat_zone = table.Column<string>(type: "text", nullable: false),
                    original_price = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    owner_email = table.Column<string>(type: "text", nullable: false),
                    owner_phone = table.Column<string>(type: "text", nullable: true),
                    owner_name = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mock_tickets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gate_access_logs_ticket_code",
                table: "gate_access_logs",
                column: "ticket_code");

            migrationBuilder.CreateIndex(
                name: "ix_mock_otps_ticket_code_owner_email_is_used",
                table: "mock_otps",
                columns: new[] { "ticket_code", "owner_email", "is_used" });

            migrationBuilder.CreateIndex(
                name: "ix_mock_tickets_ticket_code",
                table: "mock_tickets",
                column: "ticket_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gate_access_logs");

            migrationBuilder.DropTable(
                name: "mock_otps");

            migrationBuilder.DropTable(
                name: "mock_tickets");
        }
    }
}
