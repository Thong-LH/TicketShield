using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MockOrganizer.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScanIdToGateAccessLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "scan_id",
                table: "gate_access_logs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_mock_orders_buyer_email_status_created_at",
                table: "mock_orders",
                columns: new[] { "buyer_email", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_gate_access_logs_scan_id",
                table: "gate_access_logs",
                column: "scan_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mock_orders_buyer_email_status_created_at",
                table: "mock_orders");

            migrationBuilder.DropIndex(
                name: "ix_gate_access_logs_scan_id",
                table: "gate_access_logs");

            migrationBuilder.DropColumn(
                name: "scan_id",
                table: "gate_access_logs");
        }
    }
}
