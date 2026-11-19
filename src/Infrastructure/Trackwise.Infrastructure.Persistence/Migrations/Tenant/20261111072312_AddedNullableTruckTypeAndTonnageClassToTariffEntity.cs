using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackwise.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddedNullableTruckTypeAndTonnageClassToTariffEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tonnage_class",
                table: "tariffs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "truck_type",
                table: "tariffs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tonnage_class",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "truck_type",
                table: "tariffs");
        }
    }
}
