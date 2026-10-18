using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoodsManager.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class TrashAndDeletionResponses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Idempotency_Response",
                table: "IdempotencyRecords");

            migrationBuilder.CreateTable(
                name: "ProductChanges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductChanges", x => x.Id);
                    table.CheckConstraint("CK_ProductChange_Kind", "Kind IN ('created','updated','trashed','restored','purged')");
                    table.CheckConstraint("CK_ProductChange_Revision", "Revision>=1");
                    table.CheckConstraint("CK_ProductChanges_Id_Storage", "typeof(\"Id\") = 'text' AND length(\"Id\") = 36 AND instr(\"Id\", char(0)) = 0 AND substr(\"Id\",9,1) = '-' AND substr(\"Id\",14,1) = '-' AND substr(\"Id\",19,1) = '-' AND substr(\"Id\",24,1) = '-' AND length(replace(\"Id\",'-','')) = 32 AND replace(\"Id\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductChanges_Kind_Storage", "typeof(\"Kind\") = 'text' AND instr(\"Kind\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductChanges_OccurredAt_Storage", "typeof(\"OccurredAt\") = 'integer' AND \"OccurredAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductChanges_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductChanges_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Idempotency_Response",
                table: "IdempotencyRecords",
                sql: "json_valid(ResponseJson)=1 AND ((ResponseStatus IN (200,201) AND json_type(ResponseJson)='object') OR (ResponseStatus=204 AND json_type(ResponseJson)='null'))");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChanges_ProductId_OccurredAt",
                table: "ProductChanges",
                columns: new[] { "ProductId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductChanges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Idempotency_Response",
                table: "IdempotencyRecords");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Idempotency_Response",
                table: "IdempotencyRecords",
                sql: "ResponseStatus IN (200,201) AND json_valid(ResponseJson)=1 AND json_type(ResponseJson)='object'");
        }
    }
}
