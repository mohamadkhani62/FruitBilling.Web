using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FruitBilling.Web.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel_e3082dcf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SortingExitPermits_HarvestCycleId",
                table: "SortingExitPermits");

            migrationBuilder.CreateIndex(
                name: "IX_SortingExitPermits_HarvestCycleId_PermitNumber",
                table: "SortingExitPermits",
                columns: new[] { "HarvestCycleId", "PermitNumber" },
                unique: true,
                filter: "[HarvestCycleId] IS NOT NULL AND [PermitNumber] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SortingExitPermits_HarvestCycleId_PermitNumber",
                table: "SortingExitPermits");

            migrationBuilder.CreateIndex(
                name: "IX_SortingExitPermits_HarvestCycleId",
                table: "SortingExitPermits",
                column: "HarvestCycleId");
        }
    }
}
