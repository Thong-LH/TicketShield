using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayoutSellerIdAndEscrowRetryCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "seller_id",
                table: "payout_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE payout_transactions AS payout
                SET seller_id = escrow.seller_id
                FROM escrow_transactions AS escrow
                WHERE escrow.id = payout.escrow_id
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "seller_id",
                table: "payout_transactions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retry_count",
                table: "escrow_transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_payout_transactions_seller_id",
                table: "payout_transactions",
                column: "seller_id");

            migrationBuilder.AddForeignKey(
                name: "fk_payout_transactions_shadow_users_seller_id",
                table: "payout_transactions",
                column: "seller_id",
                principalTable: "shadow_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payout_transactions_shadow_users_seller_id",
                table: "payout_transactions");

            migrationBuilder.DropIndex(
                name: "ix_payout_transactions_seller_id",
                table: "payout_transactions");

            migrationBuilder.DropColumn(
                name: "seller_id",
                table: "payout_transactions");

            migrationBuilder.DropColumn(
                name: "retry_count",
                table: "escrow_transactions");
        }
    }
}
