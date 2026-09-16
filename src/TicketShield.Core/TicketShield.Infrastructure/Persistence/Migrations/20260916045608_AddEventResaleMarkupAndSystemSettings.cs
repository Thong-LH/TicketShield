using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventResaleMarkupAndSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Catch-up from Thịnh's snapshot plus US-2.6 markup. Local DBs may already
            // have auth/fee columns outside EF history, so each step is IF NOT EXISTS.
            migrationBuilder.Sql(@"
ALTER TABLE users ADD COLUMN IF NOT EXISTS google_id text;
ALTER TABLE users ADD COLUMN IF NOT EXISTS password_hash text;
ALTER TABLE users ADD COLUMN IF NOT EXISTS password_reset_otp text;
ALTER TABLE users ADD COLUMN IF NOT EXISTS password_reset_otp_expires_at timestamp with time zone;

ALTER TABLE resale_listings ADD COLUMN IF NOT EXISTS applied_markup_percentage numeric(5,2) NOT NULL DEFAULT 0;
ALTER TABLE events ADD COLUMN IF NOT EXISTS max_resale_markup_percentage numeric(5,2) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS system_settings (
    id uuid NOT NULL,
    setting_key character varying(100) NOT NULL,
    setting_value character varying(500) NOT NULL,
    data_type character varying(50) NOT NULL,
    description character varying(500) NULL,
    updated_by uuid NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_system_settings PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_resale_listings_private_access_token
    ON resale_listings (private_access_token);
CREATE UNIQUE INDEX IF NOT EXISTS ix_system_settings_setting_key
    ON system_settings (setting_key);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropIndex(
                name: "ix_resale_listings_private_access_token",
                table: "resale_listings");

            migrationBuilder.DropColumn(
                name: "google_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_reset_otp",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_reset_otp_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "applied_markup_percentage",
                table: "resale_listings");

            migrationBuilder.DropColumn(
                name: "max_resale_markup_percentage",
                table: "events");
        }
    }
}
