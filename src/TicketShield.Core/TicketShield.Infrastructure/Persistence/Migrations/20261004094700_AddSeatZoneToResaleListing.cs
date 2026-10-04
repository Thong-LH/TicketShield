using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatZoneToResaleListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "seat_zone",
                table: "resale_listings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "seat_zone",
                table: "resale_listings");
        }
    }
}
