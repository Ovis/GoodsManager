using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoodsManager.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class ProductFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: true),
                    ManufacturerId = table.Column<string>(type: "TEXT", nullable: true),
                    SellerId = table.Column<string>(type: "TEXT", nullable: true),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProductId);
                    table.CheckConstraint("CK_Product_Memo", "Memo IS NULL OR length(Memo)<=4000");
                    table.CheckConstraint("CK_Product_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Product_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Product_Version", "Version IS NULL OR length(Version)<=200");
                    table.CheckConstraint("CK_Products_CategoryId_Storage", "typeof(\"CategoryId\") = 'text' AND length(\"CategoryId\") = 36 AND instr(\"CategoryId\", char(0)) = 0 AND substr(\"CategoryId\",9,1) = '-' AND substr(\"CategoryId\",14,1) = '-' AND substr(\"CategoryId\",19,1) = '-' AND substr(\"CategoryId\",24,1) = '-' AND length(replace(\"CategoryId\",'-','')) = 32 AND replace(\"CategoryId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Products_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Products_DeletedAt_Storage", "\"DeletedAt\" IS NULL OR (typeof(\"DeletedAt\") = 'integer' AND \"DeletedAt\" BETWEEN -62135596800000 AND 253402300799999)");
                    table.CheckConstraint("CK_Products_Id_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Products_ManufacturerId_Storage", "\"ManufacturerId\" IS NULL OR (typeof(\"ManufacturerId\") = 'text' AND length(\"ManufacturerId\") = 36 AND instr(\"ManufacturerId\", char(0)) = 0 AND substr(\"ManufacturerId\",9,1) = '-' AND substr(\"ManufacturerId\",14,1) = '-' AND substr(\"ManufacturerId\",19,1) = '-' AND substr(\"ManufacturerId\",24,1) = '-' AND length(replace(\"ManufacturerId\",'-','')) = 32 AND replace(\"ManufacturerId\",'-','') NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_Products_Memo_Storage", "\"Memo\" IS NULL OR (typeof(\"Memo\") = 'text' AND instr(\"Memo\", char(0)) = 0)");
                    table.CheckConstraint("CK_Products_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Products_NormalizedName_Storage", "typeof(\"NormalizedName\") = 'text' AND instr(\"NormalizedName\", char(0)) = 0");
                    table.CheckConstraint("CK_Products_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Products_SellerId_Storage", "\"SellerId\" IS NULL OR (typeof(\"SellerId\") = 'text' AND length(\"SellerId\") = 36 AND instr(\"SellerId\", char(0)) = 0 AND substr(\"SellerId\",9,1) = '-' AND substr(\"SellerId\",14,1) = '-' AND substr(\"SellerId\",19,1) = '-' AND substr(\"SellerId\",24,1) = '-' AND length(replace(\"SellerId\",'-','')) = 32 AND replace(\"SellerId\",'-','') NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_Products_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Products_Version_Storage", "\"Version\" IS NULL OR (typeof(\"Version\") = 'text' AND instr(\"Version\", char(0)) = 0)");
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Organizations_ManufacturerId",
                        column: x => x.ManufacturerId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Organizations_SellerId",
                        column: x => x.SellerId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    InventoryItemId = table.Column<string>(type: "TEXT", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: true),
                    StorageLocation = table.Column<string>(type: "TEXT", nullable: true),
                    PurchasePlace = table.Column<string>(type: "TEXT", nullable: true),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.InventoryItemId);
                    table.CheckConstraint("CK_InventoryItems_Condition", "Condition IS NULL OR Condition IN ('unopened','opened','inUse')");
                    table.CheckConstraint("CK_InventoryItems_Condition_Storage", "\"Condition\" IS NULL OR (typeof(\"Condition\") = 'text' AND instr(\"Condition\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryItems_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_InventoryItems_Id_Storage", "typeof(\"InventoryItemId\") = 'text' AND length(\"InventoryItemId\") = 36 AND instr(\"InventoryItemId\", char(0)) = 0 AND substr(\"InventoryItemId\",9,1) = '-' AND substr(\"InventoryItemId\",14,1) = '-' AND substr(\"InventoryItemId\",19,1) = '-' AND substr(\"InventoryItemId\",24,1) = '-' AND length(replace(\"InventoryItemId\",'-','')) = 32 AND replace(\"InventoryItemId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_InventoryItems_Location", "StorageLocation IS NULL OR length(StorageLocation)<=200");
                    table.CheckConstraint("CK_InventoryItems_Memo", "Memo IS NULL OR length(Memo)<=4000");
                    table.CheckConstraint("CK_InventoryItems_Memo_Storage", "\"Memo\" IS NULL OR (typeof(\"Memo\") = 'text' AND instr(\"Memo\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryItems_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_InventoryItems_Purchase", "PurchasePlace IS NULL OR length(PurchasePlace)<=200");
                    table.CheckConstraint("CK_InventoryItems_PurchasePlace_Storage", "\"PurchasePlace\" IS NULL OR (typeof(\"PurchasePlace\") = 'text' AND instr(\"PurchasePlace\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryItems_StorageLocation_Storage", "\"StorageLocation\" IS NULL OR (typeof(\"StorageLocation\") = 'text' AND instr(\"StorageLocation\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryItems_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.ForeignKey(
                        name: "FK_InventoryItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryLots",
                columns: table => new
                {
                    InventoryLotId = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: true),
                    StorageLocation = table.Column<string>(type: "TEXT", nullable: true),
                    PurchasePlace = table.Column<string>(type: "TEXT", nullable: true),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryLots", x => x.InventoryLotId);
                    table.CheckConstraint("CK_InventoryLots_Condition", "Condition IS NULL OR Condition IN ('unopened','opened','inUse')");
                    table.CheckConstraint("CK_InventoryLots_Condition_Storage", "\"Condition\" IS NULL OR (typeof(\"Condition\") = 'text' AND instr(\"Condition\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryLots_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_InventoryLots_Id_Storage", "typeof(\"InventoryLotId\") = 'text' AND length(\"InventoryLotId\") = 36 AND instr(\"InventoryLotId\", char(0)) = 0 AND substr(\"InventoryLotId\",9,1) = '-' AND substr(\"InventoryLotId\",14,1) = '-' AND substr(\"InventoryLotId\",19,1) = '-' AND substr(\"InventoryLotId\",24,1) = '-' AND length(replace(\"InventoryLotId\",'-','')) = 32 AND replace(\"InventoryLotId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_InventoryLots_Location", "StorageLocation IS NULL OR length(StorageLocation)<=200");
                    table.CheckConstraint("CK_InventoryLots_Memo", "Memo IS NULL OR length(Memo)<=4000");
                    table.CheckConstraint("CK_InventoryLots_Memo_Storage", "\"Memo\" IS NULL OR (typeof(\"Memo\") = 'text' AND instr(\"Memo\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryLots_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_InventoryLots_Purchase", "PurchasePlace IS NULL OR length(PurchasePlace)<=200");
                    table.CheckConstraint("CK_InventoryLots_PurchasePlace_Storage", "\"PurchasePlace\" IS NULL OR (typeof(\"PurchasePlace\") = 'text' AND instr(\"PurchasePlace\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryLots_Quantity_Storage", "typeof(\"Quantity\") = 'integer'");
                    table.CheckConstraint("CK_InventoryLots_StorageLocation_Storage", "\"StorageLocation\" IS NULL OR (typeof(\"StorageLocation\") = 'text' AND instr(\"StorageLocation\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryLots_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Lot_Quantity", "Quantity BETWEEN 1 AND 2147483647");
                    table.ForeignKey(
                        name: "FK_InventoryLots_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductCharacters",
                columns: table => new
                {
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCharacters", x => new { x.ProductId, x.CharacterId });
                    table.CheckConstraint("CK_ProductCharacters_CharacterId_Storage", "typeof(\"CharacterId\") = 'text' AND length(\"CharacterId\") = 36 AND instr(\"CharacterId\", char(0)) = 0 AND substr(\"CharacterId\",9,1) = '-' AND substr(\"CharacterId\",14,1) = '-' AND substr(\"CharacterId\",19,1) = '-' AND substr(\"CharacterId\",24,1) = '-' AND length(replace(\"CharacterId\",'-','')) = 32 AND replace(\"CharacterId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductCharacters_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_ProductCharacters_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "CharacterId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCharacters_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductIdentifiers",
                columns: table => new
                {
                    ProductIdentifierId = table.Column<string>(type: "TEXT", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    Scheme = table.Column<string>(type: "TEXT", nullable: false),
                    RawValue = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedValue = table.Column<string>(type: "TEXT", nullable: false),
                    ComparisonValue = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductIdentifiers", x => x.ProductIdentifierId);
                    table.CheckConstraint("CK_Identifier_Comparison", "length(ComparisonValue)=14 AND ComparisonValue NOT GLOB '*[^0-9]*'");
                    table.CheckConstraint("CK_Identifier_Normalized", "(Scheme='ean8' AND length(NormalizedValue)=8 AND NormalizedValue NOT GLOB '*[^0-9]*') OR (Scheme='upca' AND length(NormalizedValue)=12 AND NormalizedValue NOT GLOB '*[^0-9]*') OR (Scheme IN ('ean13','isbn13') AND length(NormalizedValue)=13 AND NormalizedValue NOT GLOB '*[^0-9]*') OR (Scheme='isbn10' AND length(NormalizedValue)=10 AND substr(NormalizedValue,1,9) NOT GLOB '*[^0-9]*' AND substr(NormalizedValue,10,1) GLOB '[0-9X]')");
                    table.CheckConstraint("CK_Identifier_Raw", "length(RawValue) BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_ProductIdentifiers_ComparisonValue_Storage", "typeof(\"ComparisonValue\") = 'text' AND instr(\"ComparisonValue\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductIdentifiers_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductIdentifiers_Id_Storage", "typeof(\"ProductIdentifierId\") = 'text' AND length(\"ProductIdentifierId\") = 36 AND instr(\"ProductIdentifierId\", char(0)) = 0 AND substr(\"ProductIdentifierId\",9,1) = '-' AND substr(\"ProductIdentifierId\",14,1) = '-' AND substr(\"ProductIdentifierId\",19,1) = '-' AND substr(\"ProductIdentifierId\",24,1) = '-' AND length(replace(\"ProductIdentifierId\",'-','')) = 32 AND replace(\"ProductIdentifierId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductIdentifiers_NormalizedValue_Storage", "typeof(\"NormalizedValue\") = 'text' AND instr(\"NormalizedValue\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductIdentifiers_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductIdentifiers_RawValue_Storage", "typeof(\"RawValue\") = 'text' AND instr(\"RawValue\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductIdentifiers_Scheme_Storage", "typeof(\"Scheme\") = 'text' AND instr(\"Scheme\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductIdentifiers_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.ForeignKey(
                        name: "FK_ProductIdentifiers_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductLinks",
                columns: table => new
                {
                    ProductLinkId = table.Column<string>(type: "TEXT", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLinks", x => x.ProductLinkId);
                    table.CheckConstraint("CK_ProductLink_Url", "length(Url) BETWEEN 1 AND 2048 AND (lower(Url) GLOB 'https://?*' OR lower(Url) GLOB 'http://?*')");
                    table.CheckConstraint("CK_ProductLinks_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductLinks_Id_Storage", "typeof(\"ProductLinkId\") = 'text' AND length(\"ProductLinkId\") = 36 AND instr(\"ProductLinkId\", char(0)) = 0 AND substr(\"ProductLinkId\",9,1) = '-' AND substr(\"ProductLinkId\",14,1) = '-' AND substr(\"ProductLinkId\",19,1) = '-' AND substr(\"ProductLinkId\",24,1) = '-' AND length(replace(\"ProductLinkId\",'-','')) = 32 AND replace(\"ProductLinkId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductLinks_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductLinks_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductLinks_Url_Storage", "typeof(\"Url\") = 'text' AND instr(\"Url\", char(0)) = 0");
                    table.ForeignKey(
                        name: "FK_ProductLinks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductTags",
                columns: table => new
                {
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    TagId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTags", x => new { x.ProductId, x.TagId });
                    table.CheckConstraint("CK_ProductTags_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductTags_TagId_Storage", "typeof(\"TagId\") = 'text' AND length(\"TagId\") = 36 AND instr(\"TagId\", char(0)) = 0 AND substr(\"TagId\",9,1) = '-' AND substr(\"TagId\",14,1) = '-' AND substr(\"TagId\",19,1) = '-' AND substr(\"TagId\",24,1) = '-' AND length(replace(\"TagId\",'-','')) = 32 AND replace(\"TagId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_ProductTags_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductWorks",
                columns: table => new
                {
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductWorks", x => new { x.ProductId, x.WorkId });
                    table.CheckConstraint("CK_ProductWorks_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductWorks_WorkId_Storage", "typeof(\"WorkId\") = 'text' AND length(\"WorkId\") = 36 AND instr(\"WorkId\", char(0)) = 0 AND substr(\"WorkId\",9,1) = '-' AND substr(\"WorkId\",14,1) = '-' AND substr(\"WorkId\",19,1) = '-' AND substr(\"WorkId\",24,1) = '-' AND length(replace(\"WorkId\",'-','')) = 32 AND replace(\"WorkId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_ProductWorks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductWorks_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "WorkId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_ProductId_InventoryItemId",
                table: "InventoryItems",
                columns: new[] { "ProductId", "InventoryItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_ProductId_InventoryLotId",
                table: "InventoryLots",
                columns: new[] { "ProductId", "InventoryLotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCharacters_CharacterId_ProductId",
                table: "ProductCharacters",
                columns: new[] { "CharacterId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentifiers_ComparisonValue_ProductId",
                table: "ProductIdentifiers",
                columns: new[] { "ComparisonValue", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentifiers_ProductId_Scheme_NormalizedValue",
                table: "ProductIdentifiers",
                columns: new[] { "ProductId", "Scheme", "NormalizedValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductLinks_ProductId_ProductLinkId",
                table: "ProductLinks",
                columns: new[] { "ProductId", "ProductLinkId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_DeletedAt_NormalizedName_ProductId",
                table: "Products",
                columns: new[] { "DeletedAt", "NormalizedName", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ManufacturerId",
                table: "Products",
                column: "ManufacturerId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SellerId",
                table: "Products",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTags_TagId_ProductId",
                table: "ProductTags",
                columns: new[] { "TagId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorks_WorkId_ProductId",
                table: "ProductWorks",
                columns: new[] { "WorkId", "ProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryItems");

            migrationBuilder.DropTable(
                name: "InventoryLots");

            migrationBuilder.DropTable(
                name: "ProductCharacters");

            migrationBuilder.DropTable(
                name: "ProductIdentifiers");

            migrationBuilder.DropTable(
                name: "ProductLinks");

            migrationBuilder.DropTable(
                name: "ProductTags");

            migrationBuilder.DropTable(
                name: "ProductWorks");

            migrationBuilder.DropTable(
                name: "Products");
        }
    }
}
