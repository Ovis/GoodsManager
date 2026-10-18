using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoodsManager.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialSystemState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DataRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemState", x => x.Id);
                    table.CheckConstraint("CK_SystemState_DataRevision", "\"DataRevision\" >= 0");
                    table.CheckConstraint("CK_SystemState_DataRevision_Storage", "typeof(\"DataRevision\") = 'integer'");
                    table.CheckConstraint("CK_SystemState_Id_Storage", "typeof(\"Id\") = 'integer'");
                    table.CheckConstraint("CK_SystemState_Singleton", "\"Id\" = 1");
                    table.CheckConstraint("CK_SystemState_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                });

            migrationBuilder.InsertData(
                table: "SystemState",
                columns: new[] { "Id", "DataRevision", "UpdatedAt" },
                values: new object[] { 1, 0L, 0L });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemState");
        }
    }
}
