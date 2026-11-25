using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackwise.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddedChargeCodeIdInBookingLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "charge_code_id",
                table: "booking_charge_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_booking_charge_lines_charge_code_id",
                table: "booking_charge_lines",
                column: "charge_code_id");

            migrationBuilder.AddForeignKey(
                name: "fk_booking_charge_lines_charge_code_charge_code_id",
                table: "booking_charge_lines",
                column: "charge_code_id",
                principalTable: "charge_codes",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_booking_charge_lines_charge_code_charge_code_id",
                table: "booking_charge_lines");

            migrationBuilder.DropIndex(
                name: "ix_booking_charge_lines_charge_code_id",
                table: "booking_charge_lines");

            migrationBuilder.DropColumn(
                name: "charge_code_id",
                table: "booking_charge_lines");
        }
    }
}
