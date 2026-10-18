using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoodsManager.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class BackupFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Idempotency_Response",
                table: "IdempotencyRecords");

            migrationBuilder.CreateTable(
                name: "BackupRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    SnapshotDataRevision = table.Column<long>(type: "INTEGER", nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: true),
                    ManifestHash = table.Column<string>(type: "TEXT", nullable: true),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    RetentionManaged = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRuns", x => x.Id);
                    table.CheckConstraint("CK_BackupRun_Values", "Kind IN ('automatic','manual','preMigration','external') AND State IN ('running','succeeded','failed','deleting','deleted') AND (SnapshotDataRevision IS NULL OR SnapshotDataRevision>=0) AND (ByteLength IS NULL OR ByteLength>0)");
                    table.CheckConstraint("CK_BackupRuns_ByteLength_Storage", "\"ByteLength\" IS NULL OR (typeof(\"ByteLength\") = 'integer')");
                    table.CheckConstraint("CK_BackupRuns_CompletedAt_Storage", "\"CompletedAt\" IS NULL OR (typeof(\"CompletedAt\") = 'integer' AND \"CompletedAt\" BETWEEN -62135596800000 AND 253402300799999)");
                    table.CheckConstraint("CK_BackupRuns_ErrorCode_Storage", "\"ErrorCode\" IS NULL OR (typeof(\"ErrorCode\") = 'text' AND instr(\"ErrorCode\", char(0)) = 0)");
                    table.CheckConstraint("CK_BackupRuns_Id_Storage", "typeof(\"Id\") = 'text' AND length(\"Id\") = 36 AND instr(\"Id\", char(0)) = 0 AND substr(\"Id\",9,1) = '-' AND substr(\"Id\",14,1) = '-' AND substr(\"Id\",19,1) = '-' AND substr(\"Id\",24,1) = '-' AND length(replace(\"Id\",'-','')) = 32 AND replace(\"Id\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_BackupRuns_Kind_Storage", "typeof(\"Kind\") = 'text' AND instr(\"Kind\", char(0)) = 0");
                    table.CheckConstraint("CK_BackupRuns_ManifestHash_Storage", "\"ManifestHash\" IS NULL OR (typeof(\"ManifestHash\") = 'text' AND instr(\"ManifestHash\", char(0)) = 0)");
                    table.CheckConstraint("CK_BackupRuns_RelativePath_Storage", "\"RelativePath\" IS NULL OR (typeof(\"RelativePath\") = 'text' AND instr(\"RelativePath\", char(0)) = 0)");
                    table.CheckConstraint("CK_BackupRuns_RetentionManaged_Storage", "typeof(\"RetentionManaged\") = 'integer' AND \"RetentionManaged\" IN (0,1)");
                    table.CheckConstraint("CK_BackupRuns_SnapshotDataRevision_Storage", "\"SnapshotDataRevision\" IS NULL OR (typeof(\"SnapshotDataRevision\") = 'integer')");
                    table.CheckConstraint("CK_BackupRuns_StartedAt_Storage", "typeof(\"StartedAt\") = 'integer' AND \"StartedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_BackupRuns_State_Storage", "typeof(\"State\") = 'text' AND instr(\"State\", char(0)) = 0");
                });

            migrationBuilder.CreateTable(
                name: "BackupSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<long>(type: "INTEGER", nullable: false),
                    LocalTime = table.Column<string>(type: "TEXT", nullable: false),
                    TimeZone = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastAutomaticDate = table.Column<string>(type: "TEXT", nullable: true),
                    LastIntegrityDate = table.Column<string>(type: "TEXT", nullable: true),
                    KeyFingerprint = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSettings", x => x.Id);
                    table.CheckConstraint("CK_BackupSettings_Enabled_Storage", "typeof(\"Enabled\") = 'integer' AND \"Enabled\" IN (0,1)");
                    table.CheckConstraint("CK_BackupSettings_Id_Storage", "typeof(\"Id\") = 'integer'");
                    table.CheckConstraint("CK_BackupSettings_Key", "KeyFingerprint IS NULL OR (length(KeyFingerprint)=64 AND KeyFingerprint NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_BackupSettings_KeyFingerprint_Storage", "\"KeyFingerprint\" IS NULL OR (typeof(\"KeyFingerprint\") = 'text' AND instr(\"KeyFingerprint\", char(0)) = 0)");
                    table.CheckConstraint("CK_BackupSettings_LastAutomaticDate_Storage", "\"LastAutomaticDate\" IS NULL OR (typeof(\"LastAutomaticDate\") = 'text' AND instr(\"LastAutomaticDate\", char(0)) = 0)");
                    table.CheckConstraint("CK_BackupSettings_LastIntegrityDate_Storage", "\"LastIntegrityDate\" IS NULL OR (typeof(\"LastIntegrityDate\") = 'text' AND instr(\"LastIntegrityDate\", char(0)) = 0)");
                    table.CheckConstraint("CK_BackupSettings_LocalTime_Storage", "typeof(\"LocalTime\") = 'text' AND instr(\"LocalTime\", char(0)) = 0");
                    table.CheckConstraint("CK_BackupSettings_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_BackupSettings_TimeZone_Storage", "typeof(\"TimeZone\") = 'text' AND instr(\"TimeZone\", char(0)) = 0");
                    table.CheckConstraint("CK_BackupSettings_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_BackupSettings_Values", "Id=1 AND Revision>=1 AND TimeZone='Asia/Tokyo' AND LocalTime GLOB '[0-2][0-9]:[0-5][0-9]' AND substr(LocalTime,1,2)<'24'");
                });

            migrationBuilder.InsertData(
                table: "BackupSettings",
                columns: new[] { "Id", "Enabled", "KeyFingerprint", "LastAutomaticDate", "LastIntegrityDate", "LocalTime", "Revision", "TimeZone", "UpdatedAt" },
                values: new object[] { 1, 1L, null, null, null, "03:00", 1L, "Asia/Tokyo", 0L });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Idempotency_Response",
                table: "IdempotencyRecords",
                sql: "json_valid(ResponseJson)=1 AND ((ResponseStatus IN (200,201,202) AND json_type(ResponseJson)='object') OR (ResponseStatus=204 AND json_type(ResponseJson)='null'))");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRuns_Kind_State_CompletedAt",
                table: "BackupRuns",
                columns: new[] { "Kind", "State", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRuns_RelativePath",
                table: "BackupRuns",
                column: "RelativePath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupRuns");

            migrationBuilder.DropTable(
                name: "BackupSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Idempotency_Response",
                table: "IdempotencyRecords");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Idempotency_Response",
                table: "IdempotencyRecords",
                sql: "json_valid(ResponseJson)=1 AND ((ResponseStatus IN (200,201) AND json_type(ResponseJson)='object') OR (ResponseStatus=204 AND json_type(ResponseJson)='null'))");
        }
    }
}
