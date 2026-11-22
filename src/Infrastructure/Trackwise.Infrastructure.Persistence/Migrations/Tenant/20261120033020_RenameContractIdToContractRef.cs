using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackwise.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class RenameContractIdToContractRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tariffs_name_mode_service_level_contract_id_customer_id_is_",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "contract_id",
                table: "tariffs");

            migrationBuilder.AddColumn<string>(
                name: "contract_ref",
                table: "tariffs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tariffs_name_mode_service_level_customer_id_is_active",
                table: "tariffs",
                columns: new[] { "name", "mode", "service_level", "customer_id", "is_active" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tariffs_name_mode_service_level_customer_id_is_active",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "contract_ref",
                table: "tariffs");

            migrationBuilder.AddColumn<Guid>(
                name: "contract_id",
                table: "tariffs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tariffs_name_mode_service_level_contract_id_customer_id_is_",
                table: "tariffs",
                columns: new[] { "name", "mode", "service_level", "contract_id", "customer_id", "is_active" },
                unique: true);
        }
    }
}
