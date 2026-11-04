using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoodsManager.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class SampleCollectionsAndDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SampleStatus",
                table: "Collections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CollectionRequest",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Operation = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    ResponseJson = table.Column<string>(type: "TEXT", nullable: false),
                    Etag = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRequest", x => new { x.UserId, x.Operation, x.Key });
                    table.CheckConstraint("CK_CollectionRequest_Etag_Storage", "typeof(\"Etag\") = 'text' AND instr(\"Etag\", char(0)) = 0");
                    table.CheckConstraint("CK_CollectionRequest_ExpiresAt_Storage", "typeof(\"ExpiresAt\") = 'integer' AND \"ExpiresAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_CollectionRequest_Hash", "length(RequestHash)=64 AND RequestHash NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_CollectionRequest_Key_Storage", "typeof(\"Key\") = 'text' AND length(\"Key\") = 36 AND instr(\"Key\", char(0)) = 0 AND substr(\"Key\",9,1) = '-' AND substr(\"Key\",14,1) = '-' AND substr(\"Key\",19,1) = '-' AND substr(\"Key\",24,1) = '-' AND length(replace(\"Key\",'-','')) = 32 AND replace(\"Key\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_CollectionRequest_Operation_Storage", "typeof(\"Operation\") = 'text' AND instr(\"Operation\", char(0)) = 0");
                    table.CheckConstraint("CK_CollectionRequest_RequestHash_Storage", "typeof(\"RequestHash\") = 'text' AND instr(\"RequestHash\", char(0)) = 0");
                    table.CheckConstraint("CK_CollectionRequest_Response", "json_valid(ResponseJson)=1 AND json_type(ResponseJson)='object'");
                    table.CheckConstraint("CK_CollectionRequest_ResponseJson_Storage", "typeof(\"ResponseJson\") = 'text' AND instr(\"ResponseJson\", char(0)) = 0");
                    table.CheckConstraint("CK_CollectionRequest_Status", "StatusCode IN (200,201)");
                    table.CheckConstraint("CK_CollectionRequest_StatusCode_Storage", "typeof(\"StatusCode\") = 'integer'");
                    table.CheckConstraint("CK_CollectionRequest_UserId_Storage", "typeof(\"UserId\") = 'text' AND length(\"UserId\") = 36 AND instr(\"UserId\", char(0)) = 0 AND substr(\"UserId\",9,1) = '-' AND substr(\"UserId\",14,1) = '-' AND substr(\"UserId\",19,1) = '-' AND substr(\"UserId\",24,1) = '-' AND length(replace(\"UserId\",'-','')) = 32 AND replace(\"UserId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_CollectionRequest_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Collections",
                keyColumn: "Id",
                keyValue: "c011ec70-0000-4000-8000-000000000001",
                column: "SampleStatus",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_SampleStatus_CreatedAt",
                table: "Collections",
                columns: new[] { "SampleStatus", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Collection_SampleStatus",
                table: "Collections",
                sql: "SampleStatus IS NULL OR SampleStatus IN ('pending','ready','failed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Collections_SampleStatus_Storage",
                table: "Collections",
                sql: "\"SampleStatus\" IS NULL OR (typeof(\"SampleStatus\") = 'text' AND instr(\"SampleStatus\", char(0)) = 0)");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRequest_ExpiresAt",
                table: "CollectionRequest",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionRequest");

            migrationBuilder.DropIndex(
                name: "IX_Collections_SampleStatus_CreatedAt",
                table: "Collections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Collection_SampleStatus",
                table: "Collections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Collections_SampleStatus_Storage",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "SampleStatus",
                table: "Collections");
        }
    }
}
