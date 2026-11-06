using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketEntityAndDecoupleResaleListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ticket_id",
                table: "resale_listings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    seat_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    original_price = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    previous_ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tickets", x => x.id);
                    table.ForeignKey(
                        name: "fk_tickets_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_shadow_users_current_owner_id",
                        column: x => x.current_owner_id,
                        principalTable: "shadow_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_ticket_tiers_tier_id",
                        column: x => x.tier_id,
                        principalTable: "ticket_tiers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_tickets_previous_ticket_id",
                        column: x => x.previous_ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_resale_listings_ticket_id",
                table: "resale_listings",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_current_owner_id",
                table: "tickets",
                column: "current_owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_event_id_ticket_code",
                table: "tickets",
                columns: new[] { "event_id", "ticket_code" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_previous_ticket_id",
                table: "tickets",
                column: "previous_ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tier_id",
                table: "tickets",
                column: "tier_id");

            migrationBuilder.AddForeignKey(
                name: "fk_resale_listings_tickets_ticket_id",
                table: "resale_listings",
                column: "ticket_id",
                principalTable: "tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_resale_listings_tickets_ticket_id",
                table: "resale_listings");

            migrationBuilder.DropTable(
                name: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_resale_listings_ticket_id",
                table: "resale_listings");

            migrationBuilder.DropColumn(
                name: "ticket_id",
                table: "resale_listings");
        }
    }
}
