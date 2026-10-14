using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizerLifecycleAndExternalEventId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_events_organizer_id;");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "organizers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "official_email",
                table: "organizers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "organizers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "connection_status",
                table: "organizers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "contact_person",
                table: "organizers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_catalog_synced_at",
                table: "organizers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_ping_at",
                table: "organizers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "last_ping_latency_ms",
                table: "organizers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_code",
                table: "organizers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_event_id",
                table: "events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_events_organizer_id_external_event_id",
                table: "events",
                columns: new[] { "organizer_id", "external_event_id" },
                unique: true,
                filter: "external_event_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_events_organizer_id_external_event_id",
                table: "events");

            migrationBuilder.DropColumn(
                name: "contact_person",
                table: "organizers");

            migrationBuilder.DropColumn(
                name: "last_catalog_synced_at",
                table: "organizers");

            migrationBuilder.DropColumn(
                name: "last_ping_at",
                table: "organizers");

            migrationBuilder.DropColumn(
                name: "last_ping_latency_ms",
                table: "organizers");

            migrationBuilder.DropColumn(
                name: "tax_code",
                table: "organizers");

            migrationBuilder.DropColumn(
                name: "external_event_id",
                table: "events");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "organizers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "official_email",
                table: "organizers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "organizers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "connection_status",
                table: "organizers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "ix_events_organizer_id",
                table: "events",
                column: "organizer_id");
        }
    }
}
