using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCoreTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organizers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    official_email = table.Column<string>(type: "text", nullable: false),
                    contact_phone = table.Column<string>(type: "text", nullable: true),
                    api_key_hash = table.Column<string>(type: "text", nullable: false),
                    webhook_url = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    id_card_number = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    venue = table.Column<string>(type: "text", nullable: false),
                    event_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resale_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_events_organizers_organizer_id",
                        column: x => x.organizer_id,
                        principalTable: "organizers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_bank_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_code = table.Column<string>(type: "text", nullable: false),
                    bank_name = table.Column<string>(type: "text", nullable: false),
                    account_number = table.Column<string>(type: "text", nullable: false),
                    account_holder_name = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "ticket_tiers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_name = table.Column<string>(type: "text", nullable: false),
                    original_price = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_tiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_tiers_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resale_listings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_ticket_code = table.Column<string>(type: "text", nullable: false),
                    original_price = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    resale_price = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    is_private = table.Column<bool>(type: "boolean", nullable: false),
                    private_access_token = table.Column<string>(type: "text", nullable: true),
                    verification_status = table.Column<string>(type: "text", nullable: false),
                    listing_status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resale_listings", x => x.id);
                    table.ForeignKey(
                        name: "fk_resale_listings_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_resale_listings_ticket_tiers_tier_id",
                        column: x => x.tier_id,
                        principalTable: "ticket_tiers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_resale_listings_users_seller_id",
                        column: x => x.seller_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "escrow_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_ticket_price = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    buyer_fee = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    seller_fee = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    total_buyer_paid = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    net_seller_payout = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    payment_reference = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    unlock_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispute_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recipient_name = table.Column<string>(type: "text", nullable: true),
                    recipient_email = table.Column<string>(type: "text", nullable: true),
                    recipient_id_card = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_escrow_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_escrow_transactions_resale_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "resale_listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_escrow_transactions_users_buyer_id",
                        column: x => x.buyer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_escrow_transactions_users_seller_id",
                        column: x => x.seller_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "disputes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    escrow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispute_code = table.Column<string>(type: "text", nullable: false),
                    reason_code = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    resolution = table.Column<string>(type: "text", nullable: true),
                    refund_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    admin_notes = table.Column<string>(type: "text", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disputes", x => x.id);
                    table.ForeignKey(
                        name: "fk_disputes_escrow_transactions_escrow_id",
                        column: x => x.escrow_id,
                        principalTable: "escrow_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_disputes_users_buyer_id",
                        column: x => x.buyer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_disputes_users_resolved_by",
                        column: x => x.resolved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "payout_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    escrow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_bank_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payout_code = table.Column<string>(type: "text", nullable: false),
                    recipient_bank_code = table.Column<string>(type: "text", nullable: false),
                    recipient_account_number = table.Column<string>(type: "text", nullable: false),
                    recipient_account_name = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    bank_reference_code = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_error_message = table.Column<string>(type: "text", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payout_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_payout_transactions_escrow_transactions_escrow_id",
                        column: x => x.escrow_id,
                        principalTable: "escrow_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payout_transactions_user_bank_accounts_seller_bank_account_id",
                        column: x => x.seller_bank_account_id,
                        principalTable: "user_bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "dispute_evidences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploader_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_type = table.Column<string>(type: "text", nullable: false),
                    file_url = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispute_evidences", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispute_evidences_disputes_dispute_id",
                        column: x => x.dispute_id,
                        principalTable: "disputes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dispute_evidences_users_uploader_id",
                        column: x => x.uploader_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispute_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_role = table.Column<string>(type: "text", nullable: false),
                    message_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispute_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispute_messages_disputes_dispute_id",
                        column: x => x.dispute_id,
                        principalTable: "disputes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dispute_messages_users_sender_id",
                        column: x => x.sender_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dispute_evidences_dispute_id",
                table: "dispute_evidences",
                column: "dispute_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispute_evidences_uploader_id",
                table: "dispute_evidences",
                column: "uploader_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispute_messages_dispute_id",
                table: "dispute_messages",
                column: "dispute_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispute_messages_sender_id",
                table: "dispute_messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_buyer_id",
                table: "disputes",
                column: "buyer_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_dispute_code",
                table: "disputes",
                column: "dispute_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disputes_escrow_id",
                table: "disputes",
                column: "escrow_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disputes_resolved_by",
                table: "disputes",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "ix_escrow_transactions_buyer_id",
                table: "escrow_transactions",
                column: "buyer_id");

            migrationBuilder.CreateIndex(
                name: "ix_escrow_transactions_listing_id",
                table: "escrow_transactions",
                column: "listing_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_escrow_transactions_seller_id",
                table: "escrow_transactions",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_organizer_id",
                table: "events",
                column: "organizer_id");

            migrationBuilder.CreateIndex(
                name: "ix_organizers_official_email",
                table: "organizers",
                column: "official_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payout_transactions_escrow_id",
                table: "payout_transactions",
                column: "escrow_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payout_transactions_payout_code",
                table: "payout_transactions",
                column: "payout_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payout_transactions_seller_bank_account_id",
                table: "payout_transactions",
                column: "seller_bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_resale_listings_event_id",
                table: "resale_listings",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_resale_listings_original_ticket_code",
                table: "resale_listings",
                column: "original_ticket_code",
                unique: true,
                filter: "listing_status IN ('Verified', 'Transacting')");

            migrationBuilder.CreateIndex(
                name: "ix_resale_listings_seller_id",
                table: "resale_listings",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_resale_listings_tier_id",
                table: "resale_listings",
                column: "tier_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_tiers_event_id",
                table: "ticket_tiers",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_bank_accounts_user_id",
                table: "user_bank_accounts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispute_evidences");

            migrationBuilder.DropTable(
                name: "dispute_messages");

            migrationBuilder.DropTable(
                name: "payout_transactions");

            migrationBuilder.DropTable(
                name: "disputes");

            migrationBuilder.DropTable(
                name: "user_bank_accounts");

            migrationBuilder.DropTable(
                name: "escrow_transactions");

            migrationBuilder.DropTable(
                name: "resale_listings");

            migrationBuilder.DropTable(
                name: "ticket_tiers");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "organizers");
        }
    }
}
