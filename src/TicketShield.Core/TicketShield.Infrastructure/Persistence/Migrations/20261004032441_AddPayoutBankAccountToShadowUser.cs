using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayoutBankAccountToShadowUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payout_account_name",
                table: "shadow_users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "payout_account_number",
                table: "shadow_users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "payout_bank_code",
                table: "shadow_users",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payout_account_name",
                table: "shadow_users");

            migrationBuilder.DropColumn(
                name: "payout_account_number",
                table: "shadow_users");

            migrationBuilder.DropColumn(
                name: "payout_bank_code",
                table: "shadow_users");
        }
    }
}
