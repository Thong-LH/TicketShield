using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDisputeWaivedAtToEscrowTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispute_waived_at",
                table: "escrow_transactions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dispute_waived_at",
                table: "escrow_transactions");
        }
    }
}
