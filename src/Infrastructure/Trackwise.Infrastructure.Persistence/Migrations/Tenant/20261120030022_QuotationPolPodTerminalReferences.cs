using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackwise.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class QuotationPolPodTerminalReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "pol",
                table: "quotations",
                newName: "pol_id");

            migrationBuilder.RenameColumn(
                name: "pod",
                table: "quotations",
                newName: "pod_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotations_pod_id",
                table: "quotations",
                column: "pod_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotations_pol_id",
                table: "quotations",
                column: "pol_id");

            migrationBuilder.AddForeignKey(
                name: "fk_quotations_terminal_pod_id",
                table: "quotations",
                column: "pod_id",
                principalTable: "terminals",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_quotations_terminal_pol_id",
                table: "quotations",
                column: "pol_id",
                principalTable: "terminals",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_quotations_terminal_pod_id",
                table: "quotations");

            migrationBuilder.DropForeignKey(
                name: "fk_quotations_terminal_pol_id",
                table: "quotations");

            migrationBuilder.DropIndex(
                name: "ix_quotations_pod_id",
                table: "quotations");

            migrationBuilder.DropIndex(
                name: "ix_quotations_pol_id",
                table: "quotations");

            migrationBuilder.RenameColumn(
                name: "pol_id",
                table: "quotations",
                newName: "pol");

            migrationBuilder.RenameColumn(
                name: "pod_id",
                table: "quotations",
                newName: "pod");
        }
    }
}
