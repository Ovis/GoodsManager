using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoodsManager.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class SearchFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PurchasePlaceNormalized",
                table: "InventoryLots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageLocationNormalized",
                table: "InventoryLots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchasePlaceNormalized",
                table: "InventoryItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageLocationNormalized",
                table: "InventoryItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductSearchDocuments",
                columns: table => new
                {
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    NameNormalized = table.Column<string>(type: "TEXT", nullable: false),
                    WorksNormalized = table.Column<string>(type: "TEXT", nullable: false),
                    CharactersNormalized = table.Column<string>(type: "TEXT", nullable: false),
                    OtherNormalized = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizationVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSearchDocuments", x => x.ProductId);
                    table.CheckConstraint("CK_ProductSearchDocuments_CharactersNormalized_Storage", "typeof(\"CharactersNormalized\") = 'text' AND instr(\"CharactersNormalized\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductSearchDocuments_NameNormalized_Storage", "typeof(\"NameNormalized\") = 'text' AND instr(\"NameNormalized\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductSearchDocuments_NormalizationVersion_Storage", "typeof(\"NormalizationVersion\") = 'integer'");
                    table.CheckConstraint("CK_ProductSearchDocuments_OtherNormalized_Storage", "typeof(\"OtherNormalized\") = 'text' AND instr(\"OtherNormalized\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductSearchDocuments_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductSearchDocuments_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductSearchDocuments_WorksNormalized_Storage", "typeof(\"WorksNormalized\") = 'text' AND instr(\"WorksNormalized\", char(0)) = 0");
                    table.CheckConstraint("CK_SearchDocument_Version", "NormalizationVersion>=1");
                    table.ForeignKey(
                        name: "FK_ProductSearchDocuments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CreatedAt_ProductId",
                table: "Products",
                columns: new[] { "CreatedAt", "ProductId" },
                descending: new[] { true, false },
                filter: "DeletedAt IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_NormalizedName_ProductId",
                table: "Products",
                columns: new[] { "NormalizedName", "ProductId" },
                filter: "DeletedAt IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_UpdatedAt_ProductId",
                table: "Products",
                columns: new[] { "UpdatedAt", "ProductId" },
                descending: new[] { true, false },
                filter: "DeletedAt IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryLots_PurchasePlaceNormalized_Storage",
                table: "InventoryLots",
                sql: "\"PurchasePlaceNormalized\" IS NULL OR (typeof(\"PurchasePlaceNormalized\") = 'text' AND instr(\"PurchasePlaceNormalized\", char(0)) = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryLots_StorageLocationNormalized_Storage",
                table: "InventoryLots",
                sql: "\"StorageLocationNormalized\" IS NULL OR (typeof(\"StorageLocationNormalized\") = 'text' AND instr(\"StorageLocationNormalized\", char(0)) = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryItems_PurchasePlaceNormalized_Storage",
                table: "InventoryItems",
                sql: "\"PurchasePlaceNormalized\" IS NULL OR (typeof(\"PurchasePlaceNormalized\") = 'text' AND instr(\"PurchasePlaceNormalized\", char(0)) = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryItems_StorageLocationNormalized_Storage",
                table: "InventoryItems",
                sql: "\"StorageLocationNormalized\" IS NULL OR (typeof(\"StorageLocationNormalized\") = 'text' AND instr(\"StorageLocationNormalized\", char(0)) = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductSearchDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Products_CreatedAt_ProductId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_NormalizedName_ProductId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_UpdatedAt_ProductId",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryLots_PurchasePlaceNormalized_Storage",
                table: "InventoryLots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryLots_StorageLocationNormalized_Storage",
                table: "InventoryLots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryItems_PurchasePlaceNormalized_Storage",
                table: "InventoryItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryItems_StorageLocationNormalized_Storage",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "PurchasePlaceNormalized",
                table: "InventoryLots");

            migrationBuilder.DropColumn(
                name: "StorageLocationNormalized",
                table: "InventoryLots");

            migrationBuilder.DropColumn(
                name: "PurchasePlaceNormalized",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "StorageLocationNormalized",
                table: "InventoryItems");
        }
    }
}
