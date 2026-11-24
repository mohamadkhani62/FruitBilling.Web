using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FruitBilling.Web.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel_0af1ede1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BasketCount",
                table: "SortingBasculTickets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortingExitPermitItemId",
                table: "SortingBasculTickets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BasketCount",
                table: "BasculTickets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FruitExitPermitItemId",
                table: "BasculTickets",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FruitExitPermitItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FruitExitPermitId = table.Column<int>(type: "int", nullable: false),
                    VarietyId = table.Column<int>(type: "int", nullable: false),
                    BasketCount = table.Column<int>(type: "int", nullable: false),
                    BasketType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HarvestMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FruitExitPermitItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FruitExitPermitItems_FruitExitPermits_FruitExitPermitId",
                        column: x => x.FruitExitPermitId,
                        principalTable: "FruitExitPermits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FruitExitPermitItems_ProductVarieties_VarietyId",
                        column: x => x.VarietyId,
                        principalTable: "ProductVarieties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HarvestCycleCategories",
                columns: table => new
                {
                    HarvestCycleId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HarvestCycleCategories", x => new { x.HarvestCycleId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_HarvestCycleCategories_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HarvestCycleCategories_HarvestCycles_HarvestCycleId",
                        column: x => x.HarvestCycleId,
                        principalTable: "HarvestCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SortingExitPermitItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SortingExitPermitId = table.Column<int>(type: "int", nullable: false),
                    VarietyId = table.Column<int>(type: "int", nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BasketCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SortingExitPermitItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SortingExitPermitItems_ProductVarieties_VarietyId",
                        column: x => x.VarietyId,
                        principalTable: "ProductVarieties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SortingExitPermitItems_SortingExitPermits_SortingExitPermitId",
                        column: x => x.SortingExitPermitId,
                        principalTable: "SortingExitPermits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SortingBasculTickets_SortingExitPermitItemId",
                table: "SortingBasculTickets",
                column: "SortingExitPermitItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BasculTickets_FruitExitPermitItemId",
                table: "BasculTickets",
                column: "FruitExitPermitItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FruitExitPermitItems_FruitExitPermitId",
                table: "FruitExitPermitItems",
                column: "FruitExitPermitId");

            migrationBuilder.CreateIndex(
                name: "IX_FruitExitPermitItems_VarietyId",
                table: "FruitExitPermitItems",
                column: "VarietyId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestCycleCategories_CategoryId",
                table: "HarvestCycleCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SortingExitPermitItems_SortingExitPermitId",
                table: "SortingExitPermitItems",
                column: "SortingExitPermitId");

            migrationBuilder.CreateIndex(
                name: "IX_SortingExitPermitItems_VarietyId",
                table: "SortingExitPermitItems",
                column: "VarietyId");

            migrationBuilder.AddForeignKey(
                name: "FK_BasculTickets_FruitExitPermitItems_FruitExitPermitItemId",
                table: "BasculTickets",
                column: "FruitExitPermitItemId",
                principalTable: "FruitExitPermitItems",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SortingBasculTickets_SortingExitPermitItems_SortingExitPermitItemId",
                table: "SortingBasculTickets",
                column: "SortingExitPermitItemId",
                principalTable: "SortingExitPermitItems",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BasculTickets_FruitExitPermitItems_FruitExitPermitItemId",
                table: "BasculTickets");

            migrationBuilder.DropForeignKey(
                name: "FK_SortingBasculTickets_SortingExitPermitItems_SortingExitPermitItemId",
                table: "SortingBasculTickets");

            migrationBuilder.DropTable(
                name: "FruitExitPermitItems");

            migrationBuilder.DropTable(
                name: "HarvestCycleCategories");

            migrationBuilder.DropTable(
                name: "SortingExitPermitItems");

            migrationBuilder.DropIndex(
                name: "IX_SortingBasculTickets_SortingExitPermitItemId",
                table: "SortingBasculTickets");

            migrationBuilder.DropIndex(
                name: "IX_BasculTickets_FruitExitPermitItemId",
                table: "BasculTickets");

            migrationBuilder.DropColumn(
                name: "BasketCount",
                table: "SortingBasculTickets");

            migrationBuilder.DropColumn(
                name: "SortingExitPermitItemId",
                table: "SortingBasculTickets");

            migrationBuilder.DropColumn(
                name: "BasketCount",
                table: "BasculTickets");

            migrationBuilder.DropColumn(
                name: "FruitExitPermitItemId",
                table: "BasculTickets");
        }
    }
}
