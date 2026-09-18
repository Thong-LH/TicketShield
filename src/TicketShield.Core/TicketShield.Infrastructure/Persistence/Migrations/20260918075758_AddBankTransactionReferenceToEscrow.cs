using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankTransactionReferenceToEscrow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_dispute_evidences_users_uploader_id",
                table: "dispute_evidences");

            migrationBuilder.DropForeignKey(
                name: "fk_dispute_messages_users_sender_id",
                table: "dispute_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_disputes_users_buyer_id",
                table: "disputes");

            migrationBuilder.DropForeignKey(
                name: "fk_disputes_users_resolved_by",
                table: "disputes");

            migrationBuilder.DropForeignKey(
                name: "fk_escrow_transactions_users_buyer_id",
                table: "escrow_transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_escrow_transactions_users_seller_id",
                table: "escrow_transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_payout_transactions_user_bank_accounts_seller_bank_account_id",
                table: "payout_transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_resale_listings_users_seller_id",
                table: "resale_listings");

            migrationBuilder.DropTable(
                name: "user_bank_accounts");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropIndex(
                name: "ix_payout_transactions_seller_bank_account_id",
                table: "payout_transactions");

            migrationBuilder.AddColumn<string>(
                name: "artist",
                table: "events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "banner_url",
                table: "events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_transaction_reference",
                table: "escrow_transactions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "core_resale_records",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_core_resale_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "shadow_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shadow_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shadow_users_email",
                table: "shadow_users",
                column: "email");

            migrationBuilder.AddForeignKey(
                name: "fk_dispute_evidences_shadow_users_uploader_id",
                table: "dispute_evidences",
                column: "uploader_id",
                principalTable: "shadow_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_dispute_messages_shadow_users_sender_id",
                table: "dispute_messages",
                column: "sender_id",
                principalTable: "shadow_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_disputes_shadow_users_buyer_id",
                table: "disputes",
                column: "buyer_id",
                principalTable: "shadow_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_disputes_shadow_users_resolved_by",
                table: "disputes",
                column: "resolved_by",
                principalTable: "shadow_users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_escrow_transactions_shadow_users_buyer_id",
                table: "escrow_transactions",
                column: "buyer_id",
                principalTable: "shadow_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_escrow_transactions_shadow_users_seller_id",
                table: "escrow_transactions",
                column: "seller_id",
                principalTable: "shadow_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_resale_listings_shadow_users_seller_id",
                table: "resale_listings",
                column: "seller_id",
                principalTable: "shadow_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_dispute_evidences_shadow_users_uploader_id",
                table: "dispute_evidences");

            migrationBuilder.DropForeignKey(
                name: "fk_dispute_messages_shadow_users_sender_id",
                table: "dispute_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_disputes_shadow_users_buyer_id",
                table: "disputes");

            migrationBuilder.DropForeignKey(
                name: "fk_disputes_shadow_users_resolved_by",
                table: "disputes");

            migrationBuilder.DropForeignKey(
                name: "fk_escrow_transactions_shadow_users_buyer_id",
                table: "escrow_transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_escrow_transactions_shadow_users_seller_id",
                table: "escrow_transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_resale_listings_shadow_users_seller_id",
                table: "resale_listings");

            migrationBuilder.DropTable(
                name: "core_resale_records");

            migrationBuilder.DropTable(
                name: "shadow_users");

            migrationBuilder.DropColumn(
                name: "artist",
                table: "events");

            migrationBuilder.DropColumn(
                name: "banner_url",
                table: "events");

            migrationBuilder.DropColumn(
                name: "category",
                table: "events");

            migrationBuilder.DropColumn(
                name: "city",
                table: "events");

            migrationBuilder.DropColumn(
                name: "bank_transaction_reference",
                table: "escrow_transactions");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    google_id = table.Column<string>(type: "text", nullable: true),
                    id_card_number = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    password_reset_otp = table.Column<string>(type: "text", nullable: true),
                    password_reset_otp_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_bank_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_holder_name = table.Column<string>(type: "text", nullable: false),
                    account_number = table.Column<string>(type: "text", nullable: false),
                    bank_code = table.Column<string>(type: "text", nullable: false),
                    bank_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_bank_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payout_transactions_seller_bank_account_id",
                table: "payout_transactions",
                column: "seller_bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_bank_accounts_user_id",
                table: "user_bank_accounts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_dispute_evidences_users_uploader_id",
                table: "dispute_evidences",
                column: "uploader_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_dispute_messages_users_sender_id",
                table: "dispute_messages",
                column: "sender_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_disputes_users_buyer_id",
                table: "disputes",
                column: "buyer_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_disputes_users_resolved_by",
                table: "disputes",
                column: "resolved_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_escrow_transactions_users_buyer_id",
                table: "escrow_transactions",
                column: "buyer_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_escrow_transactions_users_seller_id",
                table: "escrow_transactions",
                column: "seller_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payout_transactions_user_bank_accounts_seller_bank_account_id",
                table: "payout_transactions",
                column: "seller_bank_account_id",
                principalTable: "user_bank_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_resale_listings_users_seller_id",
                table: "resale_listings",
                column: "seller_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
