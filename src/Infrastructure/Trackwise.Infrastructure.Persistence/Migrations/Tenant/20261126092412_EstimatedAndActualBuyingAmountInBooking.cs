using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackwise.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class EstimatedAndActualBuyingAmountInBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "actual_buying_amount",
                table: "booking_charge_lines",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "buying_status",
                table: "booking_charge_lines",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "disbursement_reference",
                table: "booking_charge_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_buying_amount",
                table: "booking_charge_lines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "expense_request_id",
                table: "booking_charge_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "official_receipt_number",
                table: "booking_charge_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reconciled_at",
                table: "booking_charge_lines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reconciled_by",
                table: "booking_charge_lines",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "actual_buying_amount",
                table: "booking_charge_lines");

            migrationBuilder.DropColumn(
                name: "buying_status",
                table: "booking_charge_lines");

            migrationBuilder.DropColumn(
                name: "disbursement_reference",
                table: "booking_charge_lines");

            migrationBuilder.DropColumn(
                name: "estimated_buying_amount",
                table: "booking_charge_lines");

            migrationBuilder.DropColumn(
                name: "expense_request_id",
                table: "booking_charge_lines");

            migrationBuilder.DropColumn(
                name: "official_receipt_number",
                table: "booking_charge_lines");

            migrationBuilder.DropColumn(
                name: "reconciled_at",
                table: "booking_charge_lines");

            migrationBuilder.DropColumn(
                name: "reconciled_by",
                table: "booking_charge_lines");
        }
    }
}
