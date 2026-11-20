using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackwise.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddBookingPolPod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pod_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pol_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_pod_id",
                table: "bookings",
                column: "pod_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_pol_id",
                table: "bookings",
                column: "pol_id");

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_terminal_pod_id",
                table: "bookings",
                column: "pod_id",
                principalTable: "terminals",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_terminal_pol_id",
                table: "bookings",
                column: "pol_id",
                principalTable: "terminals",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bookings_terminal_pod_id",
                table: "bookings");

            migrationBuilder.DropForeignKey(
                name: "fk_bookings_terminal_pol_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_pod_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_pol_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "pod_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "pol_id",
                table: "bookings");
        }
    }
}
