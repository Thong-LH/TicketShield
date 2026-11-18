using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceRequiredTicketIdOnResaleListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_resale_listings_tickets_ticket_id",
                table: "resale_listings");

            migrationBuilder.AlterColumn<Guid>(
                name: "ticket_id",
                table: "resale_listings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_resale_listings_tickets_ticket_id",
                table: "resale_listings",
                column: "ticket_id",
                principalTable: "tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_resale_listings_tickets_ticket_id",
                table: "resale_listings");

            migrationBuilder.AlterColumn<Guid>(
                name: "ticket_id",
                table: "resale_listings",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "fk_resale_listings_tickets_ticket_id",
                table: "resale_listings",
                column: "ticket_id",
                principalTable: "tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
