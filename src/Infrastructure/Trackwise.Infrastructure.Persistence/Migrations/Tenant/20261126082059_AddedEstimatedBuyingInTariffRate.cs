using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackwise.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddedEstimatedBuyingInTariffRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "buying_rate_after_free_days",
                table: "tariff_rates",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_buying_amount",
                table: "tariff_rates",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "buying_rate_after_free_days",
                table: "tariff_rates");

            migrationBuilder.DropColumn(
                name: "estimated_buying_amount",
                table: "tariff_rates");
        }
    }
}
