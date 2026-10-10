using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MockOrganizer.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGateAccessLogTicketCodeScannedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_gate_access_logs_ticket_code",
                table: "gate_access_logs");

            migrationBuilder.CreateIndex(
                name: "ix_gate_access_logs_ticket_code_scanned_at",
                table: "gate_access_logs",
                columns: new[] { "ticket_code", "scanned_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_gate_access_logs_ticket_code_scanned_at",
                table: "gate_access_logs");

            migrationBuilder.CreateIndex(
                name: "ix_gate_access_logs_ticket_code",
                table: "gate_access_logs",
                column: "ticket_code");
        }
    }
}
