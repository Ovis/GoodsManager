using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoodsManager.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class MasterFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    ParentCategoryId = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategoryId);
                    table.CheckConstraint("CK_Categories_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Categories_Id_Storage", "typeof(\"CategoryId\") = 'text' AND length(\"CategoryId\") = 36 AND instr(\"CategoryId\", char(0)) = 0 AND substr(\"CategoryId\",9,1) = '-' AND substr(\"CategoryId\",14,1) = '-' AND substr(\"CategoryId\",19,1) = '-' AND substr(\"CategoryId\",24,1) = '-' AND length(replace(\"CategoryId\",'-','')) = 32 AND replace(\"CategoryId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Categories_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Categories_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Categories_NormalizedName_Storage", "typeof(\"NormalizedName\") = 'text' AND instr(\"NormalizedName\", char(0)) = 0");
                    table.CheckConstraint("CK_Categories_ParentCategoryId_Storage", "\"ParentCategoryId\" IS NULL OR (typeof(\"ParentCategoryId\") = 'text' AND length(\"ParentCategoryId\") = 36 AND instr(\"ParentCategoryId\", char(0)) = 0 AND substr(\"ParentCategoryId\",9,1) = '-' AND substr(\"ParentCategoryId\",14,1) = '-' AND substr(\"ParentCategoryId\",19,1) = '-' AND substr(\"ParentCategoryId\",24,1) = '-' AND length(replace(\"ParentCategoryId\",'-','')) = 32 AND replace(\"ParentCategoryId\",'-','') NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_Categories_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Categories_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Categories_SortOrder_Storage", "typeof(\"SortOrder\") = 'integer'");
                    table.CheckConstraint("CK_Categories_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Category_Order", "SortOrder>=0");
                    table.CheckConstraint("CK_Category_Parent", "ParentCategoryId IS NULL OR ParentCategoryId<>CategoryId");
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "Categories",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    CharacterId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.CharacterId);
                    table.CheckConstraint("CK_Characters_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Characters_Id_Storage", "typeof(\"CharacterId\") = 'text' AND length(\"CharacterId\") = 36 AND instr(\"CharacterId\", char(0)) = 0 AND substr(\"CharacterId\",9,1) = '-' AND substr(\"CharacterId\",14,1) = '-' AND substr(\"CharacterId\",19,1) = '-' AND substr(\"CharacterId\",24,1) = '-' AND length(replace(\"CharacterId\",'-','')) = 32 AND replace(\"CharacterId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Characters_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Characters_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Characters_NormalizedName_Storage", "typeof(\"NormalizedName\") = 'text' AND instr(\"NormalizedName\", char(0)) = 0");
                    table.CheckConstraint("CK_Characters_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Characters_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Characters_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    OperationScope = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", nullable: false),
                    ResponseStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ResponseJson = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseEtag = table.Column<string>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => new { x.UserId, x.OperationScope, x.Key });
                    table.CheckConstraint("CK_Idempotency_Expiry", "ExpiresAt>CompletedAt");
                    table.CheckConstraint("CK_Idempotency_Hash", "length(RequestHash)=64 AND RequestHash NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Idempotency_Response", "ResponseStatus IN (200,201) AND json_valid(ResponseJson)=1 AND json_type(ResponseJson)='object'");
                    table.CheckConstraint("CK_IdempotencyRecords_CompletedAt_Storage", "typeof(\"CompletedAt\") = 'integer' AND \"CompletedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_IdempotencyRecords_ExpiresAt_Storage", "typeof(\"ExpiresAt\") = 'integer' AND \"ExpiresAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_IdempotencyRecords_Key_Storage", "typeof(\"Key\") = 'text' AND length(\"Key\") = 36 AND instr(\"Key\", char(0)) = 0 AND substr(\"Key\",9,1) = '-' AND substr(\"Key\",14,1) = '-' AND substr(\"Key\",19,1) = '-' AND substr(\"Key\",24,1) = '-' AND length(replace(\"Key\",'-','')) = 32 AND replace(\"Key\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_IdempotencyRecords_Location_Storage", "\"Location\" IS NULL OR (typeof(\"Location\") = 'text' AND instr(\"Location\", char(0)) = 0)");
                    table.CheckConstraint("CK_IdempotencyRecords_OperationScope_Storage", "typeof(\"OperationScope\") = 'text' AND instr(\"OperationScope\", char(0)) = 0");
                    table.CheckConstraint("CK_IdempotencyRecords_RequestHash_Storage", "typeof(\"RequestHash\") = 'text' AND instr(\"RequestHash\", char(0)) = 0");
                    table.CheckConstraint("CK_IdempotencyRecords_ResponseEtag_Storage", "typeof(\"ResponseEtag\") = 'text' AND instr(\"ResponseEtag\", char(0)) = 0");
                    table.CheckConstraint("CK_IdempotencyRecords_ResponseJson_Storage", "typeof(\"ResponseJson\") = 'text' AND instr(\"ResponseJson\", char(0)) = 0");
                    table.CheckConstraint("CK_IdempotencyRecords_ResponseStatus_Storage", "typeof(\"ResponseStatus\") = 'integer'");
                    table.CheckConstraint("CK_IdempotencyRecords_UserId_Storage", "typeof(\"UserId\") = 'text' AND length(\"UserId\") = 36 AND instr(\"UserId\", char(0)) = 0 AND substr(\"UserId\",9,1) = '-' AND substr(\"UserId\",14,1) = '-' AND substr(\"UserId\",19,1) = '-' AND substr(\"UserId\",24,1) = '-' AND length(replace(\"UserId\",'-','')) = 32 AND replace(\"UserId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_IdempotencyRecords_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    WebsiteUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Memo = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.OrganizationId);
                    table.CheckConstraint("CK_Organization_Memo", "Memo IS NULL OR length(Memo)<=4000");
                    table.CheckConstraint("CK_Organization_Url", "WebsiteUrl IS NULL OR (length(WebsiteUrl) BETWEEN 1 AND 2048 AND (lower(WebsiteUrl) GLOB 'https://?*' OR lower(WebsiteUrl) GLOB 'http://?*'))");
                    table.CheckConstraint("CK_Organizations_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Organizations_Id_Storage", "typeof(\"OrganizationId\") = 'text' AND length(\"OrganizationId\") = 36 AND instr(\"OrganizationId\", char(0)) = 0 AND substr(\"OrganizationId\",9,1) = '-' AND substr(\"OrganizationId\",14,1) = '-' AND substr(\"OrganizationId\",19,1) = '-' AND substr(\"OrganizationId\",24,1) = '-' AND length(replace(\"OrganizationId\",'-','')) = 32 AND replace(\"OrganizationId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Organizations_Memo_Storage", "\"Memo\" IS NULL OR (typeof(\"Memo\") = 'text' AND instr(\"Memo\", char(0)) = 0)");
                    table.CheckConstraint("CK_Organizations_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Organizations_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Organizations_NormalizedName_Storage", "typeof(\"NormalizedName\") = 'text' AND instr(\"NormalizedName\", char(0)) = 0");
                    table.CheckConstraint("CK_Organizations_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Organizations_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Organizations_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Organizations_WebsiteUrl_Storage", "\"WebsiteUrl\" IS NULL OR (typeof(\"WebsiteUrl\") = 'text' AND instr(\"WebsiteUrl\", char(0)) = 0)");
                });

            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    SeriesId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.SeriesId);
                    table.CheckConstraint("CK_Series_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Series_Id_Storage", "typeof(\"SeriesId\") = 'text' AND length(\"SeriesId\") = 36 AND instr(\"SeriesId\", char(0)) = 0 AND substr(\"SeriesId\",9,1) = '-' AND substr(\"SeriesId\",14,1) = '-' AND substr(\"SeriesId\",19,1) = '-' AND substr(\"SeriesId\",24,1) = '-' AND length(replace(\"SeriesId\",'-','')) = 32 AND replace(\"SeriesId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Series_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Series_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Series_NormalizedName_Storage", "typeof(\"NormalizedName\") = 'text' AND instr(\"NormalizedName\", char(0)) = 0");
                    table.CheckConstraint("CK_Series_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Series_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Series_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    TagId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.TagId);
                    table.CheckConstraint("CK_Tags_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Tags_Id_Storage", "typeof(\"TagId\") = 'text' AND length(\"TagId\") = 36 AND instr(\"TagId\", char(0)) = 0 AND substr(\"TagId\",9,1) = '-' AND substr(\"TagId\",14,1) = '-' AND substr(\"TagId\",19,1) = '-' AND substr(\"TagId\",24,1) = '-' AND length(replace(\"TagId\",'-','')) = 32 AND replace(\"TagId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Tags_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Tags_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Tags_NormalizedName_Storage", "typeof(\"NormalizedName\") = 'text' AND instr(\"NormalizedName\", char(0)) = 0");
                    table.CheckConstraint("CK_Tags_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Tags_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Tags_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                });

            migrationBuilder.CreateTable(
                name: "Works",
                columns: table => new
                {
                    WorkId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Works", x => x.WorkId);
                    table.CheckConstraint("CK_Works_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Works_Id_Storage", "typeof(\"WorkId\") = 'text' AND length(\"WorkId\") = 36 AND instr(\"WorkId\", char(0)) = 0 AND substr(\"WorkId\",9,1) = '-' AND substr(\"WorkId\",14,1) = '-' AND substr(\"WorkId\",19,1) = '-' AND substr(\"WorkId\",24,1) = '-' AND length(replace(\"WorkId\",'-','')) = 32 AND replace(\"WorkId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Works_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Works_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Works_NormalizedName_Storage", "typeof(\"NormalizedName\") = 'text' AND instr(\"NormalizedName\", char(0)) = 0");
                    table.CheckConstraint("CK_Works_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Works_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Works_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                });

            migrationBuilder.CreateTable(
                name: "CharacterWorks",
                columns: table => new
                {
                    CharacterId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterWorks", x => new { x.CharacterId, x.WorkId });
                    table.CheckConstraint("CK_CharacterWorks_CharacterId_Storage", "typeof(\"CharacterId\") = 'text' AND length(\"CharacterId\") = 36 AND instr(\"CharacterId\", char(0)) = 0 AND substr(\"CharacterId\",9,1) = '-' AND substr(\"CharacterId\",14,1) = '-' AND substr(\"CharacterId\",19,1) = '-' AND substr(\"CharacterId\",24,1) = '-' AND length(replace(\"CharacterId\",'-','')) = 32 AND replace(\"CharacterId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_CharacterWorks_WorkId_Storage", "typeof(\"WorkId\") = 'text' AND length(\"WorkId\") = 36 AND instr(\"WorkId\", char(0)) = 0 AND substr(\"WorkId\",9,1) = '-' AND substr(\"WorkId\",14,1) = '-' AND substr(\"WorkId\",19,1) = '-' AND substr(\"WorkId\",24,1) = '-' AND length(replace(\"WorkId\",'-','')) = 32 AND replace(\"WorkId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_CharacterWorks_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "CharacterId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CharacterWorks_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "WorkId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkSeries",
                columns: table => new
                {
                    WorkId = table.Column<string>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSeries", x => new { x.WorkId, x.SeriesId });
                    table.CheckConstraint("CK_WorkSeries_SeriesId_Storage", "typeof(\"SeriesId\") = 'text' AND length(\"SeriesId\") = 36 AND instr(\"SeriesId\", char(0)) = 0 AND substr(\"SeriesId\",9,1) = '-' AND substr(\"SeriesId\",14,1) = '-' AND substr(\"SeriesId\",19,1) = '-' AND substr(\"SeriesId\",24,1) = '-' AND length(replace(\"SeriesId\",'-','')) = 32 AND replace(\"SeriesId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_WorkSeries_WorkId_Storage", "typeof(\"WorkId\") = 'text' AND length(\"WorkId\") = 36 AND instr(\"WorkId\", char(0)) = 0 AND substr(\"WorkId\",9,1) = '-' AND substr(\"WorkId\",14,1) = '-' AND substr(\"WorkId\",19,1) = '-' AND substr(\"WorkId\",24,1) = '-' AND length(replace(\"WorkId\",'-','')) = 32 AND replace(\"WorkId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_WorkSeries_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "SeriesId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSeries_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "WorkId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_NormalizedName_CategoryId",
                table: "Categories",
                columns: new[] { "NormalizedName", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId_SortOrder_CategoryId",
                table: "Categories",
                columns: new[] { "ParentCategoryId", "SortOrder", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_NormalizedName_CharacterId",
                table: "Characters",
                columns: new[] { "NormalizedName", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterWorks_WorkId_CharacterId",
                table: "CharacterWorks",
                columns: new[] { "WorkId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAt",
                table: "IdempotencyRecords",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_NormalizedName_OrganizationId",
                table: "Organizations",
                columns: new[] { "NormalizedName", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Series_NormalizedName_SeriesId",
                table: "Series",
                columns: new[] { "NormalizedName", "SeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_NormalizedName_TagId",
                table: "Tags",
                columns: new[] { "NormalizedName", "TagId" });

            migrationBuilder.CreateIndex(
                name: "IX_Works_NormalizedName_WorkId",
                table: "Works",
                columns: new[] { "NormalizedName", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkSeries_SeriesId_WorkId",
                table: "WorkSeries",
                columns: new[] { "SeriesId", "WorkId" });

            // 初回移行だけ投入する通常レコード。起動時の再投入はしない。
            string[][] names =
            [
                ["アクリル・キーホルダー類", "アクリルスタンド", "アクリルキーホルダー", "ラバーキーホルダー", "メタルキーホルダー", "その他"],
                ["バッジ類", "缶バッジ", "ピンバッジ", "その他"],
                ["紙・印刷物", "クリアファイル", "ポスター", "タペストリー", "ポストカード", "ブロマイド", "色紙", "ステッカー・シール", "その他"],
                ["フィギュア・立体物", "フィギュア", "ぬいぐるみ", "その他"],
                ["書籍", "書籍", "雑誌", "漫画", "同人誌", "パンフレット", "その他"],
                ["音楽・映像", "CD", "DVD", "Blu-ray", "その他"],
                ["衣類・布製品", "Tシャツ", "その他衣類", "タオル", "バッグ", "その他"],
                ["食器・生活雑貨", "マグカップ・グラス", "食器", "文房具", "その他"],
                ["その他"]
            ];
            for (var root = 0; root < names.Length; root++)
            {
                var rootId = $"b390a000-7024-4c01-a000-{(root + 1) * 100:000000000000}";
                for (var child = 0; child < names[root].Length; child++)
                {
                    var name = names[root][child];
                    // 採用済み一覧はNFKCと空白縮約で変わらない文字列だけを含む。
                    migrationBuilder.InsertData("Categories",
                        new[] { "CategoryId", "Name", "NormalizedName", "CreatedAt", "UpdatedAt", "Revision", "ParentCategoryId", "SortOrder" },
                        new object[] { $"b390a000-7024-4c01-a000-{(root + 1) * 100 + child:000000000000}", name, name.ToLowerInvariant(), 0L, 0L, 1L, child == 0 ? null : rootId, child == 0 ? root : child - 1 });
                }
            }
            migrationBuilder.Sql("UPDATE Categories SET CreatedAt=CAST(strftime('%s','now') AS INTEGER)*1000, UpdatedAt=CAST(strftime('%s','now') AS INTEGER)*1000");
            migrationBuilder.Sql("UPDATE SystemState SET DataRevision=DataRevision+1, UpdatedAt=CAST(strftime('%s','now') AS INTEGER)*1000 WHERE Id=1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLiteのDROP TABLEもRESTRICTを検査するため、自己参照だけ先に解除する。
            migrationBuilder.Sql("UPDATE Categories SET ParentCategoryId=NULL");
            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "CharacterWorks");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "WorkSeries");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "Series");

            migrationBuilder.DropTable(
                name: "Works");
        }
    }
}
