using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizerConnectionEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "api_base_url",
                table: "organizers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "connection_status",
                table: "organizers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "grpc_endpoint",
                table: "organizers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "webhook_secret_key",
                table: "organizers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "api_base_url",
                table: "organizers");

            migrationBuilder.DropColumn(
                name: "connection_status",
                table: "organizers");

            migrationBuilder.DropColumn(
                name: "grpc_endpoint",
                table: "organizers");

            migrationBuilder.DropColumn(
                name: "webhook_secret_key",
                table: "organizers");
        }
    }
}
