using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GoodsManager.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CollectionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    IsAdministrator = table.Column<long>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<long>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PhoneNumberConfirmed = table.Column<long>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<long>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<long>(type: "INTEGER", nullable: true),
                    LockoutEnabled = table.Column<long>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.CheckConstraint("CK_AspNetUsers_AccessFailedCount_Storage", "typeof(\"AccessFailedCount\") = 'integer'");
                    table.CheckConstraint("CK_AspNetUsers_ConcurrencyStamp_Storage", "\"ConcurrencyStamp\" IS NULL OR (typeof(\"ConcurrencyStamp\") = 'text' AND instr(\"ConcurrencyStamp\", char(0)) = 0)");
                    table.CheckConstraint("CK_AspNetUsers_Email_Storage", "\"Email\" IS NULL OR (typeof(\"Email\") = 'text' AND instr(\"Email\", char(0)) = 0)");
                    table.CheckConstraint("CK_AspNetUsers_EmailConfirmed_Storage", "typeof(\"EmailConfirmed\") = 'integer' AND \"EmailConfirmed\" IN (0,1)");
                    table.CheckConstraint("CK_AspNetUsers_Id_Storage", "typeof(\"Id\") = 'text' AND length(\"Id\") = 36 AND instr(\"Id\", char(0)) = 0 AND substr(\"Id\",9,1) = '-' AND substr(\"Id\",14,1) = '-' AND substr(\"Id\",19,1) = '-' AND substr(\"Id\",24,1) = '-' AND length(replace(\"Id\",'-','')) = 32 AND replace(\"Id\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_AspNetUsers_IsAdministrator_Storage", "typeof(\"IsAdministrator\") = 'integer' AND \"IsAdministrator\" IN (0,1)");
                    table.CheckConstraint("CK_AspNetUsers_LockoutEnabled_Storage", "typeof(\"LockoutEnabled\") = 'integer' AND \"LockoutEnabled\" IN (0,1)");
                    table.CheckConstraint("CK_AspNetUsers_LockoutEnd_Storage", "\"LockoutEnd\" IS NULL OR (typeof(\"LockoutEnd\") = 'integer' AND \"LockoutEnd\" BETWEEN -62135596800000 AND 253402300799999)");
                    table.CheckConstraint("CK_AspNetUsers_NormalizedEmail_Storage", "\"NormalizedEmail\" IS NULL OR (typeof(\"NormalizedEmail\") = 'text' AND instr(\"NormalizedEmail\", char(0)) = 0)");
                    table.CheckConstraint("CK_AspNetUsers_NormalizedUserName_Storage", "typeof(\"NormalizedUserName\") = 'text' AND instr(\"NormalizedUserName\", char(0)) = 0");
                    table.CheckConstraint("CK_AspNetUsers_PasswordHash_Storage", "typeof(\"PasswordHash\") = 'text' AND instr(\"PasswordHash\", char(0)) = 0");
                    table.CheckConstraint("CK_AspNetUsers_PhoneNumber_Storage", "\"PhoneNumber\" IS NULL OR (typeof(\"PhoneNumber\") = 'text' AND instr(\"PhoneNumber\", char(0)) = 0)");
                    table.CheckConstraint("CK_AspNetUsers_PhoneNumberConfirmed_Storage", "typeof(\"PhoneNumberConfirmed\") = 'integer' AND \"PhoneNumberConfirmed\" IN (0,1)");
                    table.CheckConstraint("CK_AspNetUsers_SecurityStamp_Storage", "typeof(\"SecurityStamp\") = 'text' AND instr(\"SecurityStamp\", char(0)) = 0");
                    table.CheckConstraint("CK_AspNetUsers_TwoFactorEnabled_Storage", "typeof(\"TwoFactorEnabled\") = 'integer' AND \"TwoFactorEnabled\" IN (0,1)");
                    table.CheckConstraint("CK_AspNetUsers_UserName_Storage", "typeof(\"UserName\") = 'text' AND instr(\"UserName\", char(0)) = 0");
                    table.CheckConstraint("CK_User_Failures", "AccessFailedCount>=0");
                    table.CheckConstraint("CK_User_Name", "length(UserName) BETWEEN 1 AND 64 AND UserName NOT GLOB '*[^a-zA-Z0-9._-]*'");
                });

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

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                    table.CheckConstraint("CK_Collection_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Collection_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Collections_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Collections_Id_Storage", "typeof(\"Id\") = 'text' AND length(\"Id\") = 36 AND instr(\"Id\", char(0)) = 0 AND substr(\"Id\",9,1) = '-' AND substr(\"Id\",14,1) = '-' AND substr(\"Id\",19,1) = '-' AND substr(\"Id\",24,1) = '-' AND length(replace(\"Id\",'-','')) = 32 AND replace(\"Id\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Collections_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Collections_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Collections_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                });

            migrationBuilder.CreateTable(
                name: "ImageFiles",
                columns: table => new
                {
                    ImageFileId = table.Column<string>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    DeleteAfter = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageFiles", x => x.ImageFileId);
                    table.CheckConstraint("CK_ImageFile_Values", "ByteLength>0 AND Width>0 AND Height>0 AND MediaType IN ('image/jpeg','image/png','image/webp') AND ((State='ready' AND DeleteAfter IS NULL) OR (State='deletePending' AND DeleteAfter IS NOT NULL))");
                    table.CheckConstraint("CK_ImageFiles_ByteLength_Storage", "typeof(\"ByteLength\") = 'integer'");
                    table.CheckConstraint("CK_ImageFiles_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ImageFiles_DeleteAfter_Storage", "\"DeleteAfter\" IS NULL OR (typeof(\"DeleteAfter\") = 'integer' AND \"DeleteAfter\" BETWEEN -62135596800000 AND 253402300799999)");
                    table.CheckConstraint("CK_ImageFiles_Height_Storage", "typeof(\"Height\") = 'integer'");
                    table.CheckConstraint("CK_ImageFiles_Id_Storage", "typeof(\"ImageFileId\") = 'text' AND length(\"ImageFileId\") = 36 AND instr(\"ImageFileId\", char(0)) = 0 AND substr(\"ImageFileId\",9,1) = '-' AND substr(\"ImageFileId\",14,1) = '-' AND substr(\"ImageFileId\",19,1) = '-' AND substr(\"ImageFileId\",24,1) = '-' AND length(replace(\"ImageFileId\",'-','')) = 32 AND replace(\"ImageFileId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ImageFiles_MediaType_Storage", "typeof(\"MediaType\") = 'text' AND instr(\"MediaType\", char(0)) = 0");
                    table.CheckConstraint("CK_ImageFiles_RelativePath_Storage", "typeof(\"RelativePath\") = 'text' AND instr(\"RelativePath\", char(0)) = 0");
                    table.CheckConstraint("CK_ImageFiles_Sha256_Hash", "length(Sha256)=64 AND Sha256 NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ImageFiles_Sha256_Storage", "typeof(\"Sha256\") = 'text' AND instr(\"Sha256\", char(0)) = 0");
                    table.CheckConstraint("CK_ImageFiles_State_Storage", "typeof(\"State\") = 'text' AND instr(\"State\", char(0)) = 0");
                    table.CheckConstraint("CK_ImageFiles_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ImageFiles_Width_Storage", "typeof(\"Width\") = 'integer'");
                });

            migrationBuilder.CreateTable(
                name: "SystemAlerts",
                columns: table => new
                {
                    SystemAlertId = table.Column<string>(type: "TEXT", nullable: false),
                    AlertKey = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    SafeDetailsJson = table.Column<string>(type: "TEXT", nullable: false),
                    FirstSeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAlerts", x => x.SystemAlertId);
                    table.CheckConstraint("CK_Alert_Json", "json_valid(SafeDetailsJson)");
                    table.CheckConstraint("CK_SystemAlerts_AlertKey_Storage", "typeof(\"AlertKey\") = 'text' AND instr(\"AlertKey\", char(0)) = 0");
                    table.CheckConstraint("CK_SystemAlerts_Code_Storage", "typeof(\"Code\") = 'text' AND instr(\"Code\", char(0)) = 0");
                    table.CheckConstraint("CK_SystemAlerts_FirstSeenAt_Storage", "typeof(\"FirstSeenAt\") = 'integer' AND \"FirstSeenAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_SystemAlerts_Id_Storage", "typeof(\"SystemAlertId\") = 'text' AND length(\"SystemAlertId\") = 36 AND instr(\"SystemAlertId\", char(0)) = 0 AND substr(\"SystemAlertId\",9,1) = '-' AND substr(\"SystemAlertId\",14,1) = '-' AND substr(\"SystemAlertId\",19,1) = '-' AND substr(\"SystemAlertId\",24,1) = '-' AND length(replace(\"SystemAlertId\",'-','')) = 32 AND replace(\"SystemAlertId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_SystemAlerts_LastSeenAt_Storage", "typeof(\"LastSeenAt\") = 'integer' AND \"LastSeenAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_SystemAlerts_ResolvedAt_Storage", "\"ResolvedAt\" IS NULL OR (typeof(\"ResolvedAt\") = 'integer' AND \"ResolvedAt\" BETWEEN -62135596800000 AND 253402300799999)");
                    table.CheckConstraint("CK_SystemAlerts_SafeDetailsJson_Storage", "typeof(\"SafeDetailsJson\") = 'text' AND instr(\"SafeDetailsJson\", char(0)) = 0");
                    table.CheckConstraint("CK_SystemAlerts_Severity_Storage", "typeof(\"Severity\") = 'text' AND instr(\"Severity\", char(0)) = 0");
                });

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

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.CheckConstraint("CK_AspNetUserClaims_ClaimType_Storage", "\"ClaimType\" IS NULL OR (typeof(\"ClaimType\") = 'text' AND instr(\"ClaimType\", char(0)) = 0)");
                    table.CheckConstraint("CK_AspNetUserClaims_ClaimValue_Storage", "\"ClaimValue\" IS NULL OR (typeof(\"ClaimValue\") = 'text' AND instr(\"ClaimValue\", char(0)) = 0)");
                    table.CheckConstraint("CK_AspNetUserClaims_Id_Storage", "typeof(\"Id\") = 'integer'");
                    table.CheckConstraint("CK_AspNetUserClaims_UserId_Storage", "typeof(\"UserId\") = 'text' AND length(\"UserId\") = 36 AND instr(\"UserId\", char(0)) = 0 AND substr(\"UserId\",9,1) = '-' AND substr(\"UserId\",14,1) = '-' AND substr(\"UserId\",19,1) = '-' AND substr(\"UserId\",24,1) = '-' AND length(replace(\"UserId\",'-','')) = 32 AND replace(\"UserId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.CheckConstraint("CK_AspNetUserLogins_LoginProvider_Storage", "typeof(\"LoginProvider\") = 'text' AND instr(\"LoginProvider\", char(0)) = 0");
                    table.CheckConstraint("CK_AspNetUserLogins_ProviderDisplayName_Storage", "\"ProviderDisplayName\" IS NULL OR (typeof(\"ProviderDisplayName\") = 'text' AND instr(\"ProviderDisplayName\", char(0)) = 0)");
                    table.CheckConstraint("CK_AspNetUserLogins_ProviderKey_Storage", "typeof(\"ProviderKey\") = 'text' AND instr(\"ProviderKey\", char(0)) = 0");
                    table.CheckConstraint("CK_AspNetUserLogins_UserId_Storage", "typeof(\"UserId\") = 'text' AND length(\"UserId\") = 36 AND instr(\"UserId\", char(0)) = 0 AND substr(\"UserId\",9,1) = '-' AND substr(\"UserId\",14,1) = '-' AND substr(\"UserId\",19,1) = '-' AND substr(\"UserId\",24,1) = '-' AND length(replace(\"UserId\",'-','')) = 32 AND replace(\"UserId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserPasskeys",
                columns: table => new
                {
                    CredentialId = table.Column<byte[]>(type: "BLOB", maxLength: 1024, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserPasskeys", x => x.CredentialId);
                    table.CheckConstraint("CK_AspNetUserPasskeys_CredentialId_Storage", "typeof(\"CredentialId\") = 'blob'");
                    table.CheckConstraint("CK_AspNetUserPasskeys_UserId_Storage", "typeof(\"UserId\") = 'text' AND length(\"UserId\") = 36 AND instr(\"UserId\", char(0)) = 0 AND substr(\"UserId\",9,1) = '-' AND substr(\"UserId\",14,1) = '-' AND substr(\"UserId\",19,1) = '-' AND substr(\"UserId\",24,1) = '-' AND length(replace(\"UserId\",'-','')) = 32 AND replace(\"UserId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Passkey_CredentialId_Length", "length(CredentialId) BETWEEN 1 AND 1023");
                    table.CheckConstraint("CK_Passkey_Data", "typeof(Data)='text' AND json_valid(Data)=1 AND json_type(Data)='object'");
                    table.CheckConstraint("CK_Passkey_Name", "json_type(Data,'$.Name') IS 'text' AND length(json_extract(Data,'$.Name')) BETWEEN 1 AND 100 AND instr(json_extract(Data,'$.Name'),char(0))=0");
                    table.ForeignKey(
                        name: "FK_AspNetUserPasskeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.CheckConstraint("CK_AspNetUserTokens_LoginProvider_Storage", "typeof(\"LoginProvider\") = 'text' AND instr(\"LoginProvider\", char(0)) = 0");
                    table.CheckConstraint("CK_AspNetUserTokens_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_AspNetUserTokens_UserId_Storage", "typeof(\"UserId\") = 'text' AND length(\"UserId\") = 36 AND instr(\"UserId\", char(0)) = 0 AND substr(\"UserId\",9,1) = '-' AND substr(\"UserId\",14,1) = '-' AND substr(\"UserId\",19,1) = '-' AND substr(\"UserId\",24,1) = '-' AND length(replace(\"UserId\",'-','')) = 32 AND replace(\"UserId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_AspNetUserTokens_Value_Storage", "\"Value\" IS NULL OR (typeof(\"Value\") = 'text' AND instr(\"Value\", char(0)) = 0)");
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthTickets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedTicket = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthTickets", x => x.Id);
                    table.CheckConstraint("CK_AuthTickets_ExpiresAt_Storage", "typeof(\"ExpiresAt\") = 'integer' AND \"ExpiresAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_AuthTickets_Id_Storage", "typeof(\"Id\") = 'text' AND length(\"Id\") = 36 AND instr(\"Id\", char(0)) = 0 AND substr(\"Id\",9,1) = '-' AND substr(\"Id\",14,1) = '-' AND substr(\"Id\",19,1) = '-' AND substr(\"Id\",24,1) = '-' AND length(replace(\"Id\",'-','')) = 32 AND replace(\"Id\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_AuthTickets_ProtectedTicket_Storage", "typeof(\"ProtectedTicket\") = 'blob'");
                    table.CheckConstraint("CK_AuthTickets_UserId_Storage", "typeof(\"UserId\") = 'text' AND length(\"UserId\") = 36 AND instr(\"UserId\", char(0)) = 0 AND substr(\"UserId\",9,1) = '-' AND substr(\"UserId\",14,1) = '-' AND substr(\"UserId\",19,1) = '-' AND substr(\"UserId\",24,1) = '-' AND length(replace(\"UserId\",'-','')) = 32 AND replace(\"UserId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_AuthTickets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategoryId);
                    table.UniqueConstraint("AK_Categories_CollectionId_CategoryId", x => new { x.CollectionId, x.CategoryId });
                    table.CheckConstraint("CK_Categories_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
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
                        name: "FK_Categories_Categories_CollectionId_ParentCategoryId",
                        columns: x => new { x.CollectionId, x.ParentCategoryId },
                        principalTable: "Categories",
                        principalColumns: new[] { "CollectionId", "CategoryId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Categories_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
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
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.CharacterId);
                    table.UniqueConstraint("AK_Characters_CollectionId_CharacterId", x => new { x.CollectionId, x.CharacterId });
                    table.CheckConstraint("CK_Characters_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Characters_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Characters_Id_Storage", "typeof(\"CharacterId\") = 'text' AND length(\"CharacterId\") = 36 AND instr(\"CharacterId\", char(0)) = 0 AND substr(\"CharacterId\",9,1) = '-' AND substr(\"CharacterId\",14,1) = '-' AND substr(\"CharacterId\",19,1) = '-' AND substr(\"CharacterId\",24,1) = '-' AND length(replace(\"CharacterId\",'-','')) = 32 AND replace(\"CharacterId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Characters_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Characters_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Characters_NormalizedName_Storage", "typeof(\"NormalizedName\") = 'text' AND instr(\"NormalizedName\", char(0)) = 0");
                    table.CheckConstraint("CK_Characters_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Characters_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Characters_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.ForeignKey(
                        name: "FK_Characters_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionMembers",
                columns: table => new
                {
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionMembers", x => new { x.CollectionId, x.UserId });
                    table.CheckConstraint("CK_CollectionMember_Role", "Role IN ('manager','editor')");
                    table.CheckConstraint("CK_CollectionMembers_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_CollectionMembers_Role_Storage", "typeof(\"Role\") = 'text' AND instr(\"Role\", char(0)) = 0");
                    table.CheckConstraint("CK_CollectionMembers_UserId_Storage", "typeof(\"UserId\") = 'text' AND length(\"UserId\") = 36 AND instr(\"UserId\", char(0)) = 0 AND substr(\"UserId\",9,1) = '-' AND substr(\"UserId\",14,1) = '-' AND substr(\"UserId\",19,1) = '-' AND substr(\"UserId\",24,1) = '-' AND length(replace(\"UserId\",'-','')) = 32 AND replace(\"UserId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_CollectionMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionMembers_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    OperationScope = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001"),
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
                    table.PrimaryKey("PK_IdempotencyRecords", x => new { x.CollectionId, x.UserId, x.OperationScope, x.Key });
                    table.CheckConstraint("CK_Idempotency_Expiry", "ExpiresAt>CompletedAt");
                    table.CheckConstraint("CK_Idempotency_Hash", "length(RequestHash)=64 AND RequestHash NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Idempotency_Response", "json_valid(ResponseJson)=1 AND ((ResponseStatus IN (200,201,202) AND json_type(ResponseJson)='object') OR (ResponseStatus=204 AND json_type(ResponseJson)='null'))");
                    table.CheckConstraint("CK_IdempotencyRecords_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
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
                    table.ForeignKey(
                        name: "FK_IdempotencyRecords_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImageSettings",
                columns: table => new
                {
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001"),
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    KeepOriginal = table.Column<long>(type: "INTEGER", nullable: false),
                    DisplayMaxEdge = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayQuality = table.Column<int>(type: "INTEGER", nullable: false),
                    ThumbMaxEdge = table.Column<int>(type: "INTEGER", nullable: false),
                    ThumbQuality = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageSettings", x => x.CollectionId);
                    table.CheckConstraint("CK_ImageSettings_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ImageSettings_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ImageSettings_DisplayMaxEdge_Storage", "typeof(\"DisplayMaxEdge\") = 'integer'");
                    table.CheckConstraint("CK_ImageSettings_DisplayQuality_Storage", "typeof(\"DisplayQuality\") = 'integer'");
                    table.CheckConstraint("CK_ImageSettings_Id_Storage", "typeof(\"Id\") = 'integer'");
                    table.CheckConstraint("CK_ImageSettings_KeepOriginal_Storage", "typeof(\"KeepOriginal\") = 'integer' AND \"KeepOriginal\" IN (0,1)");
                    table.CheckConstraint("CK_ImageSettings_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_ImageSettings_Singleton", "Id=1");
                    table.CheckConstraint("CK_ImageSettings_ThumbMaxEdge_Storage", "typeof(\"ThumbMaxEdge\") = 'integer'");
                    table.CheckConstraint("CK_ImageSettings_ThumbQuality_Storage", "typeof(\"ThumbQuality\") = 'integer'");
                    table.CheckConstraint("CK_ImageSettings_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ImageSettings_Values", "DisplayMaxEdge BETWEEN 1 AND 4096 AND ThumbMaxEdge BETWEEN 1 AND 1024 AND ThumbMaxEdge<=DisplayMaxEdge AND DisplayQuality BETWEEN 1 AND 100 AND ThumbQuality BETWEEN 1 AND 100 AND Revision>=1");
                    table.ForeignKey(
                        name: "FK_ImageSettings_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
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
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.OrganizationId);
                    table.UniqueConstraint("AK_Organizations_CollectionId_OrganizationId", x => new { x.CollectionId, x.OrganizationId });
                    table.CheckConstraint("CK_Organization_Memo", "Memo IS NULL OR length(Memo)<=4000");
                    table.CheckConstraint("CK_Organization_Url", "WebsiteUrl IS NULL OR (length(WebsiteUrl) BETWEEN 1 AND 2048 AND (lower(WebsiteUrl) GLOB 'https://?*' OR lower(WebsiteUrl) GLOB 'http://?*'))");
                    table.CheckConstraint("CK_Organizations_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
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
                    table.ForeignKey(
                        name: "FK_Organizations_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductChanges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductChanges", x => x.Id);
                    table.CheckConstraint("CK_ProductChange_Kind", "Kind IN ('created','updated','trashed','restored','purged')");
                    table.CheckConstraint("CK_ProductChange_Revision", "Revision>=1");
                    table.CheckConstraint("CK_ProductChanges_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductChanges_Id_Storage", "typeof(\"Id\") = 'text' AND length(\"Id\") = 36 AND instr(\"Id\", char(0)) = 0 AND substr(\"Id\",9,1) = '-' AND substr(\"Id\",14,1) = '-' AND substr(\"Id\",19,1) = '-' AND substr(\"Id\",24,1) = '-' AND length(replace(\"Id\",'-','')) = 32 AND replace(\"Id\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductChanges_Kind_Storage", "typeof(\"Kind\") = 'text' AND instr(\"Kind\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductChanges_OccurredAt_Storage", "typeof(\"OccurredAt\") = 'integer' AND \"OccurredAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductChanges_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductChanges_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.ForeignKey(
                        name: "FK_ProductChanges_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.SeriesId);
                    table.UniqueConstraint("AK_Series_CollectionId_SeriesId", x => new { x.CollectionId, x.SeriesId });
                    table.CheckConstraint("CK_Series_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Series_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Series_Id_Storage", "typeof(\"SeriesId\") = 'text' AND length(\"SeriesId\") = 36 AND instr(\"SeriesId\", char(0)) = 0 AND substr(\"SeriesId\",9,1) = '-' AND substr(\"SeriesId\",14,1) = '-' AND substr(\"SeriesId\",19,1) = '-' AND substr(\"SeriesId\",24,1) = '-' AND length(replace(\"SeriesId\",'-','')) = 32 AND replace(\"SeriesId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Series_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Series_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Series_NormalizedName_Storage", "typeof(\"NormalizedName\") = 'text' AND instr(\"NormalizedName\", char(0)) = 0");
                    table.CheckConstraint("CK_Series_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Series_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Series_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.ForeignKey(
                        name: "FK_Series_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.TagId);
                    table.UniqueConstraint("AK_Tags_CollectionId_TagId", x => new { x.CollectionId, x.TagId });
                    table.CheckConstraint("CK_Tags_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Tags_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Tags_Id_Storage", "typeof(\"TagId\") = 'text' AND length(\"TagId\") = 36 AND instr(\"TagId\", char(0)) = 0 AND substr(\"TagId\",9,1) = '-' AND substr(\"TagId\",14,1) = '-' AND substr(\"TagId\",19,1) = '-' AND substr(\"TagId\",24,1) = '-' AND length(replace(\"TagId\",'-','')) = 32 AND replace(\"TagId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Tags_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Tags_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Tags_NormalizedName_Storage", "typeof(\"NormalizedName\") = 'text' AND instr(\"NormalizedName\", char(0)) = 0");
                    table.CheckConstraint("CK_Tags_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Tags_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Tags_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.ForeignKey(
                        name: "FK_Tags_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Works", x => x.WorkId);
                    table.UniqueConstraint("AK_Works_CollectionId_WorkId", x => new { x.CollectionId, x.WorkId });
                    table.CheckConstraint("CK_Works_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Works_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Works_Id_Storage", "typeof(\"WorkId\") = 'text' AND length(\"WorkId\") = 36 AND instr(\"WorkId\", char(0)) = 0 AND substr(\"WorkId\",9,1) = '-' AND substr(\"WorkId\",14,1) = '-' AND substr(\"WorkId\",19,1) = '-' AND substr(\"WorkId\",24,1) = '-' AND length(replace(\"WorkId\",'-','')) = 32 AND replace(\"WorkId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Works_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Works_Name_Storage", "typeof(\"Name\") = 'text' AND instr(\"Name\", char(0)) = 0");
                    table.CheckConstraint("CK_Works_NormalizedName_Storage", "typeof(\"NormalizedName\") = 'text' AND instr(\"NormalizedName\", char(0)) = 0");
                    table.CheckConstraint("CK_Works_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Works_Revision_Storage", "typeof(\"Revision\") = 'integer'");
                    table.CheckConstraint("CK_Works_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.ForeignKey(
                        name: "FK_Works_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImageAssets",
                columns: table => new
                {
                    ImageAssetId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyHash = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyJson = table.Column<string>(type: "TEXT", nullable: false),
                    KeepOriginal = table.Column<long>(type: "INTEGER", nullable: false),
                    OriginalFileId = table.Column<string>(type: "TEXT", nullable: true),
                    DisplayFileId = table.Column<string>(type: "TEXT", nullable: false),
                    ThumbnailFileId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageAssets", x => x.ImageAssetId);
                    table.UniqueConstraint("AK_ImageAssets_CollectionId_ImageAssetId", x => new { x.CollectionId, x.ImageAssetId });
                    table.CheckConstraint("CK_ImageAsset_Original", "(KeepOriginal=1 AND OriginalFileId IS NOT NULL) OR (KeepOriginal=0 AND OriginalFileId IS NULL)");
                    table.CheckConstraint("CK_ImageAsset_PolicyJson", "json_valid(PolicyJson)");
                    table.CheckConstraint("CK_ImageAssets_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ImageAssets_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ImageAssets_DisplayFileId_Storage", "typeof(\"DisplayFileId\") = 'text' AND length(\"DisplayFileId\") = 36 AND instr(\"DisplayFileId\", char(0)) = 0 AND substr(\"DisplayFileId\",9,1) = '-' AND substr(\"DisplayFileId\",14,1) = '-' AND substr(\"DisplayFileId\",19,1) = '-' AND substr(\"DisplayFileId\",24,1) = '-' AND length(replace(\"DisplayFileId\",'-','')) = 32 AND replace(\"DisplayFileId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ImageAssets_Id_Storage", "typeof(\"ImageAssetId\") = 'text' AND length(\"ImageAssetId\") = 36 AND instr(\"ImageAssetId\", char(0)) = 0 AND substr(\"ImageAssetId\",9,1) = '-' AND substr(\"ImageAssetId\",14,1) = '-' AND substr(\"ImageAssetId\",19,1) = '-' AND substr(\"ImageAssetId\",24,1) = '-' AND length(replace(\"ImageAssetId\",'-','')) = 32 AND replace(\"ImageAssetId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ImageAssets_KeepOriginal_Storage", "typeof(\"KeepOriginal\") = 'integer' AND \"KeepOriginal\" IN (0,1)");
                    table.CheckConstraint("CK_ImageAssets_OriginalFileId_Storage", "\"OriginalFileId\" IS NULL OR (typeof(\"OriginalFileId\") = 'text' AND length(\"OriginalFileId\") = 36 AND instr(\"OriginalFileId\", char(0)) = 0 AND substr(\"OriginalFileId\",9,1) = '-' AND substr(\"OriginalFileId\",14,1) = '-' AND substr(\"OriginalFileId\",19,1) = '-' AND substr(\"OriginalFileId\",24,1) = '-' AND length(replace(\"OriginalFileId\",'-','')) = 32 AND replace(\"OriginalFileId\",'-','') NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_ImageAssets_PolicyHash_Hash", "length(PolicyHash)=64 AND PolicyHash NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ImageAssets_PolicyHash_Storage", "typeof(\"PolicyHash\") = 'text' AND instr(\"PolicyHash\", char(0)) = 0");
                    table.CheckConstraint("CK_ImageAssets_PolicyJson_Storage", "typeof(\"PolicyJson\") = 'text' AND instr(\"PolicyJson\", char(0)) = 0");
                    table.CheckConstraint("CK_ImageAssets_SourceSha256_Hash", "length(SourceSha256)=64 AND SourceSha256 NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ImageAssets_SourceSha256_Storage", "typeof(\"SourceSha256\") = 'text' AND instr(\"SourceSha256\", char(0)) = 0");
                    table.CheckConstraint("CK_ImageAssets_ThumbnailFileId_Storage", "typeof(\"ThumbnailFileId\") = 'text' AND length(\"ThumbnailFileId\") = 36 AND instr(\"ThumbnailFileId\", char(0)) = 0 AND substr(\"ThumbnailFileId\",9,1) = '-' AND substr(\"ThumbnailFileId\",14,1) = '-' AND substr(\"ThumbnailFileId\",19,1) = '-' AND substr(\"ThumbnailFileId\",24,1) = '-' AND length(replace(\"ThumbnailFileId\",'-','')) = 32 AND replace(\"ThumbnailFileId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_ImageAssets_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImageAssets_ImageFiles_DisplayFileId",
                        column: x => x.DisplayFileId,
                        principalTable: "ImageFiles",
                        principalColumn: "ImageFileId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImageAssets_ImageFiles_OriginalFileId",
                        column: x => x.OriginalFileId,
                        principalTable: "ImageFiles",
                        principalColumn: "ImageFileId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImageAssets_ImageFiles_ThumbnailFileId",
                        column: x => x.ThumbnailFileId,
                        principalTable: "ImageFiles",
                        principalColumn: "ImageFileId",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProductId);
                    table.UniqueConstraint("AK_Products_CollectionId_ProductId", x => new { x.CollectionId, x.ProductId });
                    table.CheckConstraint("CK_Product_Memo", "Memo IS NULL OR length(Memo)<=4000");
                    table.CheckConstraint("CK_Product_Name", "length(Name) BETWEEN 1 AND 200 AND length(trim(Name,char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)))>0");
                    table.CheckConstraint("CK_Product_Revision", "Revision>=1");
                    table.CheckConstraint("CK_Product_Version", "Version IS NULL OR length(Version)<=200");
                    table.CheckConstraint("CK_Products_CategoryId_Storage", "typeof(\"CategoryId\") = 'text' AND length(\"CategoryId\") = 36 AND instr(\"CategoryId\", char(0)) = 0 AND substr(\"CategoryId\",9,1) = '-' AND substr(\"CategoryId\",14,1) = '-' AND substr(\"CategoryId\",19,1) = '-' AND substr(\"CategoryId\",24,1) = '-' AND length(replace(\"CategoryId\",'-','')) = 32 AND replace(\"CategoryId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Products_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
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
                        name: "FK_Products_Categories_CollectionId_CategoryId",
                        columns: x => new { x.CollectionId, x.CategoryId },
                        principalTable: "Categories",
                        principalColumns: new[] { "CollectionId", "CategoryId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Organizations_CollectionId_ManufacturerId",
                        columns: x => new { x.CollectionId, x.ManufacturerId },
                        principalTable: "Organizations",
                        principalColumns: new[] { "CollectionId", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Organizations_CollectionId_SellerId",
                        columns: x => new { x.CollectionId, x.SellerId },
                        principalTable: "Organizations",
                        principalColumns: new[] { "CollectionId", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CharacterWorks",
                columns: table => new
                {
                    CharacterId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkId = table.Column<string>(type: "TEXT", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterWorks", x => new { x.CharacterId, x.WorkId });
                    table.CheckConstraint("CK_CharacterWorks_CharacterId_Storage", "typeof(\"CharacterId\") = 'text' AND length(\"CharacterId\") = 36 AND instr(\"CharacterId\", char(0)) = 0 AND substr(\"CharacterId\",9,1) = '-' AND substr(\"CharacterId\",14,1) = '-' AND substr(\"CharacterId\",19,1) = '-' AND substr(\"CharacterId\",24,1) = '-' AND length(replace(\"CharacterId\",'-','')) = 32 AND replace(\"CharacterId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_CharacterWorks_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_CharacterWorks_WorkId_Storage", "typeof(\"WorkId\") = 'text' AND length(\"WorkId\") = 36 AND instr(\"WorkId\", char(0)) = 0 AND substr(\"WorkId\",9,1) = '-' AND substr(\"WorkId\",14,1) = '-' AND substr(\"WorkId\",19,1) = '-' AND substr(\"WorkId\",24,1) = '-' AND length(replace(\"WorkId\",'-','')) = 32 AND replace(\"WorkId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_CharacterWorks_Characters_CollectionId_CharacterId",
                        columns: x => new { x.CollectionId, x.CharacterId },
                        principalTable: "Characters",
                        principalColumns: new[] { "CollectionId", "CharacterId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CharacterWorks_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CharacterWorks_Works_CollectionId_WorkId",
                        columns: x => new { x.CollectionId, x.WorkId },
                        principalTable: "Works",
                        principalColumns: new[] { "CollectionId", "WorkId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkSeries",
                columns: table => new
                {
                    WorkId = table.Column<string>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<string>(type: "TEXT", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSeries", x => new { x.WorkId, x.SeriesId });
                    table.CheckConstraint("CK_WorkSeries_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_WorkSeries_SeriesId_Storage", "typeof(\"SeriesId\") = 'text' AND length(\"SeriesId\") = 36 AND instr(\"SeriesId\", char(0)) = 0 AND substr(\"SeriesId\",9,1) = '-' AND substr(\"SeriesId\",14,1) = '-' AND substr(\"SeriesId\",19,1) = '-' AND substr(\"SeriesId\",24,1) = '-' AND length(replace(\"SeriesId\",'-','')) = 32 AND replace(\"SeriesId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_WorkSeries_WorkId_Storage", "typeof(\"WorkId\") = 'text' AND length(\"WorkId\") = 36 AND instr(\"WorkId\", char(0)) = 0 AND substr(\"WorkId\",9,1) = '-' AND substr(\"WorkId\",14,1) = '-' AND substr(\"WorkId\",19,1) = '-' AND substr(\"WorkId\",24,1) = '-' AND length(replace(\"WorkId\",'-','')) = 32 AND replace(\"WorkId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_WorkSeries_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSeries_Series_CollectionId_SeriesId",
                        columns: x => new { x.CollectionId, x.SeriesId },
                        principalTable: "Series",
                        principalColumns: new[] { "CollectionId", "SeriesId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSeries_Works_CollectionId_WorkId",
                        columns: x => new { x.CollectionId, x.WorkId },
                        principalTable: "Works",
                        principalColumns: new[] { "CollectionId", "WorkId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemporaryUploads",
                columns: table => new
                {
                    TemporaryUploadId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClientUploadKey = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    StagingPath = table.Column<string>(type: "TEXT", nullable: true),
                    SourceSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ImageAssetId = table.Column<string>(type: "TEXT", nullable: true),
                    PolicyJson = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyHash = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReplayUntil = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    ConsumedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryUploads", x => x.TemporaryUploadId);
                    table.CheckConstraint("CK_TemporaryUploads_ByteLength_Storage", "typeof(\"ByteLength\") = 'integer'");
                    table.CheckConstraint("CK_TemporaryUploads_ClientUploadKey_Storage", "typeof(\"ClientUploadKey\") = 'text' AND length(\"ClientUploadKey\") = 36 AND instr(\"ClientUploadKey\", char(0)) = 0 AND substr(\"ClientUploadKey\",9,1) = '-' AND substr(\"ClientUploadKey\",14,1) = '-' AND substr(\"ClientUploadKey\",19,1) = '-' AND substr(\"ClientUploadKey\",24,1) = '-' AND length(replace(\"ClientUploadKey\",'-','')) = 32 AND replace(\"ClientUploadKey\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_TemporaryUploads_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_TemporaryUploads_ConsumedAt_Storage", "\"ConsumedAt\" IS NULL OR (typeof(\"ConsumedAt\") = 'integer' AND \"ConsumedAt\" BETWEEN -62135596800000 AND 253402300799999)");
                    table.CheckConstraint("CK_TemporaryUploads_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_TemporaryUploads_ErrorCode_Storage", "\"ErrorCode\" IS NULL OR (typeof(\"ErrorCode\") = 'text' AND instr(\"ErrorCode\", char(0)) = 0)");
                    table.CheckConstraint("CK_TemporaryUploads_ExpiresAt_Storage", "typeof(\"ExpiresAt\") = 'integer' AND \"ExpiresAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_TemporaryUploads_Id_Storage", "typeof(\"TemporaryUploadId\") = 'text' AND length(\"TemporaryUploadId\") = 36 AND instr(\"TemporaryUploadId\", char(0)) = 0 AND substr(\"TemporaryUploadId\",9,1) = '-' AND substr(\"TemporaryUploadId\",14,1) = '-' AND substr(\"TemporaryUploadId\",19,1) = '-' AND substr(\"TemporaryUploadId\",24,1) = '-' AND length(replace(\"TemporaryUploadId\",'-','')) = 32 AND replace(\"TemporaryUploadId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_TemporaryUploads_ImageAssetId_Storage", "\"ImageAssetId\" IS NULL OR (typeof(\"ImageAssetId\") = 'text' AND length(\"ImageAssetId\") = 36 AND instr(\"ImageAssetId\", char(0)) = 0 AND substr(\"ImageAssetId\",9,1) = '-' AND substr(\"ImageAssetId\",14,1) = '-' AND substr(\"ImageAssetId\",19,1) = '-' AND substr(\"ImageAssetId\",24,1) = '-' AND length(replace(\"ImageAssetId\",'-','')) = 32 AND replace(\"ImageAssetId\",'-','') NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_TemporaryUploads_PolicyHash_Hash", "length(PolicyHash)=64 AND PolicyHash NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_TemporaryUploads_PolicyHash_Storage", "typeof(\"PolicyHash\") = 'text' AND instr(\"PolicyHash\", char(0)) = 0");
                    table.CheckConstraint("CK_TemporaryUploads_PolicyJson_Storage", "typeof(\"PolicyJson\") = 'text' AND instr(\"PolicyJson\", char(0)) = 0");
                    table.CheckConstraint("CK_TemporaryUploads_ReplayUntil_Storage", "typeof(\"ReplayUntil\") = 'integer' AND \"ReplayUntil\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_TemporaryUploads_SourceSha256_Hash", "length(SourceSha256)=64 AND SourceSha256 NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_TemporaryUploads_SourceSha256_Storage", "typeof(\"SourceSha256\") = 'text' AND instr(\"SourceSha256\", char(0)) = 0");
                    table.CheckConstraint("CK_TemporaryUploads_StagingPath_Storage", "\"StagingPath\" IS NULL OR (typeof(\"StagingPath\") = 'text' AND instr(\"StagingPath\", char(0)) = 0)");
                    table.CheckConstraint("CK_TemporaryUploads_State_Storage", "typeof(\"State\") = 'text' AND instr(\"State\", char(0)) = 0");
                    table.CheckConstraint("CK_TemporaryUploads_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_TemporaryUploads_UserId_Storage", "typeof(\"UserId\") = 'text' AND length(\"UserId\") = 36 AND instr(\"UserId\", char(0)) = 0 AND substr(\"UserId\",9,1) = '-' AND substr(\"UserId\",14,1) = '-' AND substr(\"UserId\",19,1) = '-' AND substr(\"UserId\",24,1) = '-' AND length(replace(\"UserId\",'-','')) = 32 AND replace(\"UserId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_Upload_Json", "json_valid(PolicyJson)");
                    table.CheckConstraint("CK_Upload_Values", "ByteLength>0 AND State IN ('pending','processing','ready','failed','consumed','cancelled') AND (State NOT IN ('pending','processing') OR StagingPath IS NOT NULL) AND (State!='ready' OR ImageAssetId IS NOT NULL) AND (State!='consumed' OR ConsumedAt IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TemporaryUploads_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryUploads_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryUploads_ImageAssets_CollectionId_ImageAssetId",
                        columns: x => new { x.CollectionId, x.ImageAssetId },
                        principalTable: "ImageAssets",
                        principalColumns: new[] { "CollectionId", "ImageAssetId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    InventoryItemId = table.Column<string>(type: "TEXT", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001"),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: true),
                    StorageLocation = table.Column<string>(type: "TEXT", nullable: true),
                    PurchasePlace = table.Column<string>(type: "TEXT", nullable: true),
                    StorageLocationNormalized = table.Column<string>(type: "TEXT", nullable: true),
                    PurchasePlaceNormalized = table.Column<string>(type: "TEXT", nullable: true),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.InventoryItemId);
                    table.UniqueConstraint("AK_InventoryItems_CollectionId_InventoryItemId", x => new { x.CollectionId, x.InventoryItemId });
                    table.CheckConstraint("CK_InventoryItems_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
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
                    table.CheckConstraint("CK_InventoryItems_PurchasePlaceNormalized_Storage", "\"PurchasePlaceNormalized\" IS NULL OR (typeof(\"PurchasePlaceNormalized\") = 'text' AND instr(\"PurchasePlaceNormalized\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryItems_StorageLocation_Storage", "\"StorageLocation\" IS NULL OR (typeof(\"StorageLocation\") = 'text' AND instr(\"StorageLocation\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryItems_StorageLocationNormalized_Storage", "\"StorageLocationNormalized\" IS NULL OR (typeof(\"StorageLocationNormalized\") = 'text' AND instr(\"StorageLocationNormalized\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryItems_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.ForeignKey(
                        name: "FK_InventoryItems_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryItems_Products_CollectionId_ProductId",
                        columns: x => new { x.CollectionId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CollectionId", "ProductId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryLots",
                columns: table => new
                {
                    InventoryLotId = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001"),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: true),
                    StorageLocation = table.Column<string>(type: "TEXT", nullable: true),
                    PurchasePlace = table.Column<string>(type: "TEXT", nullable: true),
                    StorageLocationNormalized = table.Column<string>(type: "TEXT", nullable: true),
                    PurchasePlaceNormalized = table.Column<string>(type: "TEXT", nullable: true),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryLots", x => x.InventoryLotId);
                    table.CheckConstraint("CK_InventoryLots_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
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
                    table.CheckConstraint("CK_InventoryLots_PurchasePlaceNormalized_Storage", "\"PurchasePlaceNormalized\" IS NULL OR (typeof(\"PurchasePlaceNormalized\") = 'text' AND instr(\"PurchasePlaceNormalized\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryLots_Quantity_Storage", "typeof(\"Quantity\") = 'integer'");
                    table.CheckConstraint("CK_InventoryLots_StorageLocation_Storage", "\"StorageLocation\" IS NULL OR (typeof(\"StorageLocation\") = 'text' AND instr(\"StorageLocation\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryLots_StorageLocationNormalized_Storage", "\"StorageLocationNormalized\" IS NULL OR (typeof(\"StorageLocationNormalized\") = 'text' AND instr(\"StorageLocationNormalized\", char(0)) = 0)");
                    table.CheckConstraint("CK_InventoryLots_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_Lot_Quantity", "Quantity BETWEEN 1 AND 2147483647");
                    table.ForeignKey(
                        name: "FK_InventoryLots_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLots_Products_CollectionId_ProductId",
                        columns: x => new { x.CollectionId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CollectionId", "ProductId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductCharacters",
                columns: table => new
                {
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<string>(type: "TEXT", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCharacters", x => new { x.ProductId, x.CharacterId });
                    table.CheckConstraint("CK_ProductCharacters_CharacterId_Storage", "typeof(\"CharacterId\") = 'text' AND length(\"CharacterId\") = 36 AND instr(\"CharacterId\", char(0)) = 0 AND substr(\"CharacterId\",9,1) = '-' AND substr(\"CharacterId\",14,1) = '-' AND substr(\"CharacterId\",19,1) = '-' AND substr(\"CharacterId\",24,1) = '-' AND length(replace(\"CharacterId\",'-','')) = 32 AND replace(\"CharacterId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductCharacters_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductCharacters_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_ProductCharacters_Characters_CollectionId_CharacterId",
                        columns: x => new { x.CollectionId, x.CharacterId },
                        principalTable: "Characters",
                        principalColumns: new[] { "CollectionId", "CharacterId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCharacters_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCharacters_Products_CollectionId_ProductId",
                        columns: x => new { x.CollectionId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CollectionId", "ProductId" },
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
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductIdentifiers", x => x.ProductIdentifierId);
                    table.CheckConstraint("CK_Identifier_Comparison", "length(ComparisonValue)=14 AND ComparisonValue NOT GLOB '*[^0-9]*'");
                    table.CheckConstraint("CK_Identifier_Normalized", "(Scheme='ean8' AND length(NormalizedValue)=8 AND NormalizedValue NOT GLOB '*[^0-9]*') OR (Scheme='upca' AND length(NormalizedValue)=12 AND NormalizedValue NOT GLOB '*[^0-9]*') OR (Scheme IN ('ean13','isbn13') AND length(NormalizedValue)=13 AND NormalizedValue NOT GLOB '*[^0-9]*') OR (Scheme='isbn10' AND length(NormalizedValue)=10 AND substr(NormalizedValue,1,9) NOT GLOB '*[^0-9]*' AND substr(NormalizedValue,10,1) GLOB '[0-9X]')");
                    table.CheckConstraint("CK_Identifier_Raw", "length(RawValue) BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_ProductIdentifiers_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductIdentifiers_ComparisonValue_Storage", "typeof(\"ComparisonValue\") = 'text' AND instr(\"ComparisonValue\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductIdentifiers_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductIdentifiers_Id_Storage", "typeof(\"ProductIdentifierId\") = 'text' AND length(\"ProductIdentifierId\") = 36 AND instr(\"ProductIdentifierId\", char(0)) = 0 AND substr(\"ProductIdentifierId\",9,1) = '-' AND substr(\"ProductIdentifierId\",14,1) = '-' AND substr(\"ProductIdentifierId\",19,1) = '-' AND substr(\"ProductIdentifierId\",24,1) = '-' AND length(replace(\"ProductIdentifierId\",'-','')) = 32 AND replace(\"ProductIdentifierId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductIdentifiers_NormalizedValue_Storage", "typeof(\"NormalizedValue\") = 'text' AND instr(\"NormalizedValue\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductIdentifiers_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductIdentifiers_RawValue_Storage", "typeof(\"RawValue\") = 'text' AND instr(\"RawValue\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductIdentifiers_Scheme_Storage", "typeof(\"Scheme\") = 'text' AND instr(\"Scheme\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductIdentifiers_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.ForeignKey(
                        name: "FK_ProductIdentifiers_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductIdentifiers_Products_CollectionId_ProductId",
                        columns: x => new { x.CollectionId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CollectionId", "ProductId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    ProductImageId = table.Column<string>(type: "TEXT", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    ImageAssetId = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPrimary = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.ProductImageId);
                    table.CheckConstraint("CK_ProductImage_Sort", "SortOrder>=0");
                    table.CheckConstraint("CK_ProductImages_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductImages_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductImages_Id_Storage", "typeof(\"ProductImageId\") = 'text' AND length(\"ProductImageId\") = 36 AND instr(\"ProductImageId\", char(0)) = 0 AND substr(\"ProductImageId\",9,1) = '-' AND substr(\"ProductImageId\",14,1) = '-' AND substr(\"ProductImageId\",19,1) = '-' AND substr(\"ProductImageId\",24,1) = '-' AND length(replace(\"ProductImageId\",'-','')) = 32 AND replace(\"ProductImageId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductImages_ImageAssetId_Storage", "typeof(\"ImageAssetId\") = 'text' AND length(\"ImageAssetId\") = 36 AND instr(\"ImageAssetId\", char(0)) = 0 AND substr(\"ImageAssetId\",9,1) = '-' AND substr(\"ImageAssetId\",14,1) = '-' AND substr(\"ImageAssetId\",19,1) = '-' AND substr(\"ImageAssetId\",24,1) = '-' AND length(replace(\"ImageAssetId\",'-','')) = 32 AND replace(\"ImageAssetId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductImages_IsPrimary_Storage", "typeof(\"IsPrimary\") = 'integer' AND \"IsPrimary\" IN (0,1)");
                    table.CheckConstraint("CK_ProductImages_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductImages_SortOrder_Storage", "typeof(\"SortOrder\") = 'integer'");
                    table.CheckConstraint("CK_ProductImages_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.ForeignKey(
                        name: "FK_ProductImages_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductImages_ImageAssets_CollectionId_ImageAssetId",
                        columns: x => new { x.CollectionId, x.ImageAssetId },
                        principalTable: "ImageAssets",
                        principalColumns: new[] { "CollectionId", "ImageAssetId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_CollectionId_ProductId",
                        columns: x => new { x.CollectionId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CollectionId", "ProductId" },
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
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLinks", x => x.ProductLinkId);
                    table.CheckConstraint("CK_ProductLink_Url", "length(Url) BETWEEN 1 AND 2048 AND (lower(Url) GLOB 'https://?*' OR lower(Url) GLOB 'http://?*')");
                    table.CheckConstraint("CK_ProductLinks_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductLinks_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductLinks_Id_Storage", "typeof(\"ProductLinkId\") = 'text' AND length(\"ProductLinkId\") = 36 AND instr(\"ProductLinkId\", char(0)) = 0 AND substr(\"ProductLinkId\",9,1) = '-' AND substr(\"ProductLinkId\",14,1) = '-' AND substr(\"ProductLinkId\",19,1) = '-' AND substr(\"ProductLinkId\",24,1) = '-' AND length(replace(\"ProductLinkId\",'-','')) = 32 AND replace(\"ProductLinkId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductLinks_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductLinks_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductLinks_Url_Storage", "typeof(\"Url\") = 'text' AND instr(\"Url\", char(0)) = 0");
                    table.ForeignKey(
                        name: "FK_ProductLinks_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductLinks_Products_CollectionId_ProductId",
                        columns: x => new { x.CollectionId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CollectionId", "ProductId" },
                        onDelete: ReferentialAction.Cascade);
                });

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
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSearchDocuments", x => x.ProductId);
                    table.CheckConstraint("CK_ProductSearchDocuments_CharactersNormalized_Storage", "typeof(\"CharactersNormalized\") = 'text' AND instr(\"CharactersNormalized\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductSearchDocuments_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductSearchDocuments_NameNormalized_Storage", "typeof(\"NameNormalized\") = 'text' AND instr(\"NameNormalized\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductSearchDocuments_NormalizationVersion_Storage", "typeof(\"NormalizationVersion\") = 'integer'");
                    table.CheckConstraint("CK_ProductSearchDocuments_OtherNormalized_Storage", "typeof(\"OtherNormalized\") = 'text' AND instr(\"OtherNormalized\", char(0)) = 0");
                    table.CheckConstraint("CK_ProductSearchDocuments_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductSearchDocuments_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductSearchDocuments_WorksNormalized_Storage", "typeof(\"WorksNormalized\") = 'text' AND instr(\"WorksNormalized\", char(0)) = 0");
                    table.CheckConstraint("CK_SearchDocument_Version", "NormalizationVersion>=1");
                    table.ForeignKey(
                        name: "FK_ProductSearchDocuments_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductSearchDocuments_Products_CollectionId_ProductId",
                        columns: x => new { x.CollectionId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CollectionId", "ProductId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductTags",
                columns: table => new
                {
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    TagId = table.Column<string>(type: "TEXT", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTags", x => new { x.ProductId, x.TagId });
                    table.CheckConstraint("CK_ProductTags_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductTags_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductTags_TagId_Storage", "typeof(\"TagId\") = 'text' AND length(\"TagId\") = 36 AND instr(\"TagId\", char(0)) = 0 AND substr(\"TagId\",9,1) = '-' AND substr(\"TagId\",14,1) = '-' AND substr(\"TagId\",19,1) = '-' AND substr(\"TagId\",24,1) = '-' AND length(replace(\"TagId\",'-','')) = 32 AND replace(\"TagId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_ProductTags_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductTags_Products_CollectionId_ProductId",
                        columns: x => new { x.CollectionId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CollectionId", "ProductId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductTags_Tags_CollectionId_TagId",
                        columns: x => new { x.CollectionId, x.TagId },
                        principalTable: "Tags",
                        principalColumns: new[] { "CollectionId", "TagId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductWorks",
                columns: table => new
                {
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkId = table.Column<string>(type: "TEXT", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductWorks", x => new { x.ProductId, x.WorkId });
                    table.CheckConstraint("CK_ProductWorks_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductWorks_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductWorks_WorkId_Storage", "typeof(\"WorkId\") = 'text' AND length(\"WorkId\") = 36 AND instr(\"WorkId\", char(0)) = 0 AND substr(\"WorkId\",9,1) = '-' AND substr(\"WorkId\",14,1) = '-' AND substr(\"WorkId\",19,1) = '-' AND substr(\"WorkId\",24,1) = '-' AND length(replace(\"WorkId\",'-','')) = 32 AND replace(\"WorkId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_ProductWorks_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductWorks_Products_CollectionId_ProductId",
                        columns: x => new { x.CollectionId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CollectionId", "ProductId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductWorks_Works_CollectionId_WorkId",
                        columns: x => new { x.CollectionId, x.WorkId },
                        principalTable: "Works",
                        principalColumns: new[] { "CollectionId", "WorkId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryItemImages",
                columns: table => new
                {
                    InventoryItemImageId = table.Column<string>(type: "TEXT", nullable: false),
                    InventoryItemId = table.Column<string>(type: "TEXT", nullable: false),
                    ImageAssetId = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "c011ec70-0000-4000-8000-000000000001")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItemImages", x => x.InventoryItemImageId);
                    table.CheckConstraint("CK_InventoryItemImages_CollectionId_Storage", "typeof(\"CollectionId\") = 'text' AND length(\"CollectionId\") = 36 AND instr(\"CollectionId\", char(0)) = 0 AND substr(\"CollectionId\",9,1) = '-' AND substr(\"CollectionId\",14,1) = '-' AND substr(\"CollectionId\",19,1) = '-' AND substr(\"CollectionId\",24,1) = '-' AND length(replace(\"CollectionId\",'-','')) = 32 AND replace(\"CollectionId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_InventoryItemImages_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_InventoryItemImages_Id_Storage", "typeof(\"InventoryItemImageId\") = 'text' AND length(\"InventoryItemImageId\") = 36 AND instr(\"InventoryItemImageId\", char(0)) = 0 AND substr(\"InventoryItemImageId\",9,1) = '-' AND substr(\"InventoryItemImageId\",14,1) = '-' AND substr(\"InventoryItemImageId\",19,1) = '-' AND substr(\"InventoryItemImageId\",24,1) = '-' AND length(replace(\"InventoryItemImageId\",'-','')) = 32 AND replace(\"InventoryItemImageId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_InventoryItemImages_ImageAssetId_Storage", "typeof(\"ImageAssetId\") = 'text' AND length(\"ImageAssetId\") = 36 AND instr(\"ImageAssetId\", char(0)) = 0 AND substr(\"ImageAssetId\",9,1) = '-' AND substr(\"ImageAssetId\",14,1) = '-' AND substr(\"ImageAssetId\",19,1) = '-' AND substr(\"ImageAssetId\",24,1) = '-' AND length(replace(\"ImageAssetId\",'-','')) = 32 AND replace(\"ImageAssetId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_InventoryItemImages_InventoryItemId_Storage", "typeof(\"InventoryItemId\") = 'text' AND length(\"InventoryItemId\") = 36 AND instr(\"InventoryItemId\", char(0)) = 0 AND substr(\"InventoryItemId\",9,1) = '-' AND substr(\"InventoryItemId\",14,1) = '-' AND substr(\"InventoryItemId\",19,1) = '-' AND substr(\"InventoryItemId\",24,1) = '-' AND length(replace(\"InventoryItemId\",'-','')) = 32 AND replace(\"InventoryItemId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_InventoryItemImages_SortOrder_Storage", "typeof(\"SortOrder\") = 'integer'");
                    table.CheckConstraint("CK_InventoryItemImages_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ItemImage_Sort", "SortOrder>=0");
                    table.ForeignKey(
                        name: "FK_InventoryItemImages_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryItemImages_ImageAssets_CollectionId_ImageAssetId",
                        columns: x => new { x.CollectionId, x.ImageAssetId },
                        principalTable: "ImageAssets",
                        principalColumns: new[] { "CollectionId", "ImageAssetId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryItemImages_InventoryItems_CollectionId_InventoryItemId",
                        columns: x => new { x.CollectionId, x.InventoryItemId },
                        principalTable: "InventoryItems",
                        principalColumns: new[] { "CollectionId", "InventoryItemId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "BackupSettings",
                columns: new[] { "Id", "Enabled", "KeyFingerprint", "LastAutomaticDate", "LastIntegrityDate", "LocalTime", "Revision", "TimeZone", "UpdatedAt" },
                values: new object[] { 1, 1L, null, null, null, "03:00", 1L, "Asia/Tokyo", 0L });

            migrationBuilder.InsertData(
                table: "Collections",
                columns: new[] { "Id", "CreatedAt", "Name", "Revision", "UpdatedAt" },
                values: new object[] { "c011ec70-0000-4000-8000-000000000001", 0L, "マイコレクション", 1L, 0L });

            migrationBuilder.InsertData(
                table: "SystemState",
                columns: new[] { "Id", "DataRevision", "UpdatedAt" },
                values: new object[] { 1, 0L, 0L });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "CategoryId", "CollectionId", "CreatedAt", "Name", "NormalizedName", "ParentCategoryId", "Revision", "SortOrder", "UpdatedAt" },
                values: new object[,]
                {
                    { "b390a000-7024-4c01-a000-000000000100", "c011ec70-0000-4000-8000-000000000001", 0L, "アクリル・キーホルダー類", "アクリル・キーホルダー類", null, 1L, 0, 0L },
                    { "b390a000-7024-4c01-a000-000000000200", "c011ec70-0000-4000-8000-000000000001", 0L, "バッジ類", "バッジ類", null, 1L, 1, 0L },
                    { "b390a000-7024-4c01-a000-000000000300", "c011ec70-0000-4000-8000-000000000001", 0L, "紙・印刷物", "紙・印刷物", null, 1L, 2, 0L },
                    { "b390a000-7024-4c01-a000-000000000400", "c011ec70-0000-4000-8000-000000000001", 0L, "フィギュア・立体物", "フィギュア・立体物", null, 1L, 3, 0L },
                    { "b390a000-7024-4c01-a000-000000000500", "c011ec70-0000-4000-8000-000000000001", 0L, "書籍", "書籍", null, 1L, 4, 0L },
                    { "b390a000-7024-4c01-a000-000000000600", "c011ec70-0000-4000-8000-000000000001", 0L, "音楽・映像", "音楽・映像", null, 1L, 5, 0L },
                    { "b390a000-7024-4c01-a000-000000000700", "c011ec70-0000-4000-8000-000000000001", 0L, "衣類・布製品", "衣類・布製品", null, 1L, 6, 0L },
                    { "b390a000-7024-4c01-a000-000000000800", "c011ec70-0000-4000-8000-000000000001", 0L, "食器・生活雑貨", "食器・生活雑貨", null, 1L, 7, 0L },
                    { "b390a000-7024-4c01-a000-000000000900", "c011ec70-0000-4000-8000-000000000001", 0L, "その他", "その他", null, 1L, 8, 0L },
                    { "b390a000-7024-4c01-a000-000000000101", "c011ec70-0000-4000-8000-000000000001", 0L, "アクリルスタンド", "アクリルスタンド", "b390a000-7024-4c01-a000-000000000100", 1L, 0, 0L },
                    { "b390a000-7024-4c01-a000-000000000102", "c011ec70-0000-4000-8000-000000000001", 0L, "アクリルキーホルダー", "アクリルキーホルダー", "b390a000-7024-4c01-a000-000000000100", 1L, 1, 0L },
                    { "b390a000-7024-4c01-a000-000000000103", "c011ec70-0000-4000-8000-000000000001", 0L, "ラバーキーホルダー", "ラバーキーホルダー", "b390a000-7024-4c01-a000-000000000100", 1L, 2, 0L },
                    { "b390a000-7024-4c01-a000-000000000104", "c011ec70-0000-4000-8000-000000000001", 0L, "メタルキーホルダー", "メタルキーホルダー", "b390a000-7024-4c01-a000-000000000100", 1L, 3, 0L },
                    { "b390a000-7024-4c01-a000-000000000105", "c011ec70-0000-4000-8000-000000000001", 0L, "その他", "その他", "b390a000-7024-4c01-a000-000000000100", 1L, 4, 0L },
                    { "b390a000-7024-4c01-a000-000000000201", "c011ec70-0000-4000-8000-000000000001", 0L, "缶バッジ", "缶バッジ", "b390a000-7024-4c01-a000-000000000200", 1L, 0, 0L },
                    { "b390a000-7024-4c01-a000-000000000202", "c011ec70-0000-4000-8000-000000000001", 0L, "ピンバッジ", "ピンバッジ", "b390a000-7024-4c01-a000-000000000200", 1L, 1, 0L },
                    { "b390a000-7024-4c01-a000-000000000203", "c011ec70-0000-4000-8000-000000000001", 0L, "その他", "その他", "b390a000-7024-4c01-a000-000000000200", 1L, 2, 0L },
                    { "b390a000-7024-4c01-a000-000000000301", "c011ec70-0000-4000-8000-000000000001", 0L, "クリアファイル", "クリアファイル", "b390a000-7024-4c01-a000-000000000300", 1L, 0, 0L },
                    { "b390a000-7024-4c01-a000-000000000302", "c011ec70-0000-4000-8000-000000000001", 0L, "ポスター", "ポスター", "b390a000-7024-4c01-a000-000000000300", 1L, 1, 0L },
                    { "b390a000-7024-4c01-a000-000000000303", "c011ec70-0000-4000-8000-000000000001", 0L, "タペストリー", "タペストリー", "b390a000-7024-4c01-a000-000000000300", 1L, 2, 0L },
                    { "b390a000-7024-4c01-a000-000000000304", "c011ec70-0000-4000-8000-000000000001", 0L, "ポストカード", "ポストカード", "b390a000-7024-4c01-a000-000000000300", 1L, 3, 0L },
                    { "b390a000-7024-4c01-a000-000000000305", "c011ec70-0000-4000-8000-000000000001", 0L, "ブロマイド", "ブロマイド", "b390a000-7024-4c01-a000-000000000300", 1L, 4, 0L },
                    { "b390a000-7024-4c01-a000-000000000306", "c011ec70-0000-4000-8000-000000000001", 0L, "色紙", "色紙", "b390a000-7024-4c01-a000-000000000300", 1L, 5, 0L },
                    { "b390a000-7024-4c01-a000-000000000307", "c011ec70-0000-4000-8000-000000000001", 0L, "ステッカー・シール", "ステッカー・シール", "b390a000-7024-4c01-a000-000000000300", 1L, 6, 0L },
                    { "b390a000-7024-4c01-a000-000000000308", "c011ec70-0000-4000-8000-000000000001", 0L, "その他", "その他", "b390a000-7024-4c01-a000-000000000300", 1L, 7, 0L },
                    { "b390a000-7024-4c01-a000-000000000401", "c011ec70-0000-4000-8000-000000000001", 0L, "フィギュア", "フィギュア", "b390a000-7024-4c01-a000-000000000400", 1L, 0, 0L },
                    { "b390a000-7024-4c01-a000-000000000402", "c011ec70-0000-4000-8000-000000000001", 0L, "ぬいぐるみ", "ぬいぐるみ", "b390a000-7024-4c01-a000-000000000400", 1L, 1, 0L },
                    { "b390a000-7024-4c01-a000-000000000403", "c011ec70-0000-4000-8000-000000000001", 0L, "その他", "その他", "b390a000-7024-4c01-a000-000000000400", 1L, 2, 0L },
                    { "b390a000-7024-4c01-a000-000000000501", "c011ec70-0000-4000-8000-000000000001", 0L, "書籍", "書籍", "b390a000-7024-4c01-a000-000000000500", 1L, 0, 0L },
                    { "b390a000-7024-4c01-a000-000000000502", "c011ec70-0000-4000-8000-000000000001", 0L, "雑誌", "雑誌", "b390a000-7024-4c01-a000-000000000500", 1L, 1, 0L },
                    { "b390a000-7024-4c01-a000-000000000503", "c011ec70-0000-4000-8000-000000000001", 0L, "漫画", "漫画", "b390a000-7024-4c01-a000-000000000500", 1L, 2, 0L },
                    { "b390a000-7024-4c01-a000-000000000504", "c011ec70-0000-4000-8000-000000000001", 0L, "同人誌", "同人誌", "b390a000-7024-4c01-a000-000000000500", 1L, 3, 0L },
                    { "b390a000-7024-4c01-a000-000000000505", "c011ec70-0000-4000-8000-000000000001", 0L, "パンフレット", "パンフレット", "b390a000-7024-4c01-a000-000000000500", 1L, 4, 0L },
                    { "b390a000-7024-4c01-a000-000000000506", "c011ec70-0000-4000-8000-000000000001", 0L, "その他", "その他", "b390a000-7024-4c01-a000-000000000500", 1L, 5, 0L },
                    { "b390a000-7024-4c01-a000-000000000601", "c011ec70-0000-4000-8000-000000000001", 0L, "CD", "cd", "b390a000-7024-4c01-a000-000000000600", 1L, 0, 0L },
                    { "b390a000-7024-4c01-a000-000000000602", "c011ec70-0000-4000-8000-000000000001", 0L, "DVD", "dvd", "b390a000-7024-4c01-a000-000000000600", 1L, 1, 0L },
                    { "b390a000-7024-4c01-a000-000000000603", "c011ec70-0000-4000-8000-000000000001", 0L, "Blu-ray", "blu-ray", "b390a000-7024-4c01-a000-000000000600", 1L, 2, 0L },
                    { "b390a000-7024-4c01-a000-000000000604", "c011ec70-0000-4000-8000-000000000001", 0L, "その他", "その他", "b390a000-7024-4c01-a000-000000000600", 1L, 3, 0L },
                    { "b390a000-7024-4c01-a000-000000000701", "c011ec70-0000-4000-8000-000000000001", 0L, "Tシャツ", "tシャツ", "b390a000-7024-4c01-a000-000000000700", 1L, 0, 0L },
                    { "b390a000-7024-4c01-a000-000000000702", "c011ec70-0000-4000-8000-000000000001", 0L, "その他衣類", "その他衣類", "b390a000-7024-4c01-a000-000000000700", 1L, 1, 0L },
                    { "b390a000-7024-4c01-a000-000000000703", "c011ec70-0000-4000-8000-000000000001", 0L, "タオル", "タオル", "b390a000-7024-4c01-a000-000000000700", 1L, 2, 0L },
                    { "b390a000-7024-4c01-a000-000000000704", "c011ec70-0000-4000-8000-000000000001", 0L, "バッグ", "バッグ", "b390a000-7024-4c01-a000-000000000700", 1L, 3, 0L },
                    { "b390a000-7024-4c01-a000-000000000705", "c011ec70-0000-4000-8000-000000000001", 0L, "その他", "その他", "b390a000-7024-4c01-a000-000000000700", 1L, 4, 0L },
                    { "b390a000-7024-4c01-a000-000000000801", "c011ec70-0000-4000-8000-000000000001", 0L, "マグカップ・グラス", "マグカップ・グラス", "b390a000-7024-4c01-a000-000000000800", 1L, 0, 0L },
                    { "b390a000-7024-4c01-a000-000000000802", "c011ec70-0000-4000-8000-000000000001", 0L, "食器", "食器", "b390a000-7024-4c01-a000-000000000800", 1L, 1, 0L },
                    { "b390a000-7024-4c01-a000-000000000803", "c011ec70-0000-4000-8000-000000000001", 0L, "文房具", "文房具", "b390a000-7024-4c01-a000-000000000800", 1L, 2, 0L },
                    { "b390a000-7024-4c01-a000-000000000804", "c011ec70-0000-4000-8000-000000000001", 0L, "その他", "その他", "b390a000-7024-4c01-a000-000000000800", 1L, 3, 0L }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserPasskeys_UserId",
                table: "AspNetUserPasskeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthTickets_ExpiresAt",
                table: "AuthTickets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthTickets_UserId",
                table: "AuthTickets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRuns_Kind_State_CompletedAt",
                table: "BackupRuns",
                columns: new[] { "Kind", "State", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRuns_RelativePath",
                table: "BackupRuns",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_CollectionId",
                table: "Categories",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_CollectionId_ParentCategoryId",
                table: "Categories",
                columns: new[] { "CollectionId", "ParentCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_NormalizedName_CategoryId",
                table: "Categories",
                columns: new[] { "NormalizedName", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId_SortOrder_CategoryId",
                table: "Categories",
                columns: new[] { "ParentCategoryId", "SortOrder", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_CollectionId",
                table: "Characters",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_NormalizedName_CharacterId",
                table: "Characters",
                columns: new[] { "NormalizedName", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterWorks_CollectionId",
                table: "CharacterWorks",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterWorks_CollectionId_CharacterId",
                table: "CharacterWorks",
                columns: new[] { "CollectionId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterWorks_CollectionId_WorkId",
                table: "CharacterWorks",
                columns: new[] { "CollectionId", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterWorks_WorkId_CharacterId",
                table: "CharacterWorks",
                columns: new[] { "WorkId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionMembers_UserId_CollectionId",
                table: "CollectionMembers",
                columns: new[] { "UserId", "CollectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_CollectionId",
                table: "IdempotencyRecords",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAt",
                table: "IdempotencyRecords",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_UserId",
                table: "IdempotencyRecords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_CollectionId",
                table: "ImageAssets",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_CollectionId_SourceSha256_PolicyHash",
                table: "ImageAssets",
                columns: new[] { "CollectionId", "SourceSha256", "PolicyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_DisplayFileId",
                table: "ImageAssets",
                column: "DisplayFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_OriginalFileId",
                table: "ImageAssets",
                column: "OriginalFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_ThumbnailFileId",
                table: "ImageAssets",
                column: "ThumbnailFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageFiles_RelativePath",
                table: "ImageFiles",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageFiles_Sha256",
                table: "ImageFiles",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageFiles_State_DeleteAfter",
                table: "ImageFiles",
                columns: new[] { "State", "DeleteAfter" });

            migrationBuilder.CreateIndex(
                name: "IX_ImageSettings_CollectionId",
                table: "ImageSettings",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItemImages_CollectionId",
                table: "InventoryItemImages",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItemImages_CollectionId_ImageAssetId",
                table: "InventoryItemImages",
                columns: new[] { "CollectionId", "ImageAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItemImages_CollectionId_InventoryItemId",
                table: "InventoryItemImages",
                columns: new[] { "CollectionId", "InventoryItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItemImages_InventoryItemId_ImageAssetId",
                table: "InventoryItemImages",
                columns: new[] { "InventoryItemId", "ImageAssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItemImages_InventoryItemId_SortOrder_InventoryItemImageId",
                table: "InventoryItemImages",
                columns: new[] { "InventoryItemId", "SortOrder", "InventoryItemImageId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_CollectionId",
                table: "InventoryItems",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_CollectionId_ProductId",
                table: "InventoryItems",
                columns: new[] { "CollectionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_ProductId_InventoryItemId",
                table: "InventoryItems",
                columns: new[] { "ProductId", "InventoryItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_CollectionId",
                table: "InventoryLots",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_CollectionId_ProductId",
                table: "InventoryLots",
                columns: new[] { "CollectionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_ProductId_InventoryLotId",
                table: "InventoryLots",
                columns: new[] { "ProductId", "InventoryLotId" });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_CollectionId",
                table: "Organizations",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_NormalizedName_OrganizationId",
                table: "Organizations",
                columns: new[] { "NormalizedName", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductChanges_CollectionId",
                table: "ProductChanges",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChanges_ProductId_OccurredAt",
                table: "ProductChanges",
                columns: new[] { "ProductId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCharacters_CharacterId_ProductId",
                table: "ProductCharacters",
                columns: new[] { "CharacterId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCharacters_CollectionId",
                table: "ProductCharacters",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCharacters_CollectionId_CharacterId",
                table: "ProductCharacters",
                columns: new[] { "CollectionId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCharacters_CollectionId_ProductId",
                table: "ProductCharacters",
                columns: new[] { "CollectionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentifiers_CollectionId",
                table: "ProductIdentifiers",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentifiers_CollectionId_ProductId",
                table: "ProductIdentifiers",
                columns: new[] { "CollectionId", "ProductId" });

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
                name: "IX_ProductImages_CollectionId",
                table: "ProductImages",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_CollectionId_ImageAssetId",
                table: "ProductImages",
                columns: new[] { "CollectionId", "ImageAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_CollectionId_ProductId",
                table: "ProductImages",
                columns: new[] { "CollectionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId",
                table: "ProductImages",
                column: "ProductId",
                unique: true,
                filter: "IsPrimary=1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId_ImageAssetId",
                table: "ProductImages",
                columns: new[] { "ProductId", "ImageAssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId_SortOrder_ProductImageId",
                table: "ProductImages",
                columns: new[] { "ProductId", "SortOrder", "ProductImageId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductLinks_CollectionId",
                table: "ProductLinks",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductLinks_CollectionId_ProductId",
                table: "ProductLinks",
                columns: new[] { "CollectionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductLinks_ProductId_ProductLinkId",
                table: "ProductLinks",
                columns: new[] { "ProductId", "ProductLinkId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CollectionId",
                table: "Products",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CollectionId_CategoryId",
                table: "Products",
                columns: new[] { "CollectionId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CollectionId_ManufacturerId",
                table: "Products",
                columns: new[] { "CollectionId", "ManufacturerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CollectionId_SellerId",
                table: "Products",
                columns: new[] { "CollectionId", "SellerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CreatedAt_ProductId",
                table: "Products",
                columns: new[] { "CreatedAt", "ProductId" },
                descending: new[] { true, false },
                filter: "DeletedAt IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_DeletedAt_NormalizedName_ProductId",
                table: "Products",
                columns: new[] { "DeletedAt", "NormalizedName", "ProductId" });

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

            migrationBuilder.CreateIndex(
                name: "IX_ProductSearchDocuments_CollectionId",
                table: "ProductSearchDocuments",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSearchDocuments_CollectionId_ProductId",
                table: "ProductSearchDocuments",
                columns: new[] { "CollectionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductTags_CollectionId",
                table: "ProductTags",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTags_CollectionId_ProductId",
                table: "ProductTags",
                columns: new[] { "CollectionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductTags_CollectionId_TagId",
                table: "ProductTags",
                columns: new[] { "CollectionId", "TagId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductTags_TagId_ProductId",
                table: "ProductTags",
                columns: new[] { "TagId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorks_CollectionId",
                table: "ProductWorks",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorks_CollectionId_ProductId",
                table: "ProductWorks",
                columns: new[] { "CollectionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorks_CollectionId_WorkId",
                table: "ProductWorks",
                columns: new[] { "CollectionId", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorks_WorkId_ProductId",
                table: "ProductWorks",
                columns: new[] { "WorkId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_Series_CollectionId",
                table: "Series",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Series_NormalizedName_SeriesId",
                table: "Series",
                columns: new[] { "NormalizedName", "SeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_AlertKey",
                table: "SystemAlerts",
                column: "AlertKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_ResolvedAt",
                table: "SystemAlerts",
                column: "ResolvedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_CollectionId",
                table: "Tags",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_NormalizedName_TagId",
                table: "Tags",
                columns: new[] { "NormalizedName", "TagId" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryUploads_CollectionId",
                table: "TemporaryUploads",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryUploads_CollectionId_ImageAssetId",
                table: "TemporaryUploads",
                columns: new[] { "CollectionId", "ImageAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryUploads_CollectionId_UserId_ClientUploadKey",
                table: "TemporaryUploads",
                columns: new[] { "CollectionId", "UserId", "ClientUploadKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryUploads_ReplayUntil",
                table: "TemporaryUploads",
                column: "ReplayUntil");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryUploads_State_ExpiresAt",
                table: "TemporaryUploads",
                columns: new[] { "State", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryUploads_UserId",
                table: "TemporaryUploads",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Works_CollectionId",
                table: "Works",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Works_NormalizedName_WorkId",
                table: "Works",
                columns: new[] { "NormalizedName", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkSeries_CollectionId",
                table: "WorkSeries",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSeries_CollectionId_SeriesId",
                table: "WorkSeries",
                columns: new[] { "CollectionId", "SeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkSeries_CollectionId_WorkId",
                table: "WorkSeries",
                columns: new[] { "CollectionId", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkSeries_SeriesId_WorkId",
                table: "WorkSeries",
                columns: new[] { "SeriesId", "WorkId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserPasskeys");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuthTickets");

            migrationBuilder.DropTable(
                name: "BackupRuns");

            migrationBuilder.DropTable(
                name: "BackupSettings");

            migrationBuilder.DropTable(
                name: "CharacterWorks");

            migrationBuilder.DropTable(
                name: "CollectionMembers");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "ImageSettings");

            migrationBuilder.DropTable(
                name: "InventoryItemImages");

            migrationBuilder.DropTable(
                name: "InventoryLots");

            migrationBuilder.DropTable(
                name: "ProductChanges");

            migrationBuilder.DropTable(
                name: "ProductCharacters");

            migrationBuilder.DropTable(
                name: "ProductIdentifiers");

            migrationBuilder.DropTable(
                name: "ProductImages");

            migrationBuilder.DropTable(
                name: "ProductLinks");

            migrationBuilder.DropTable(
                name: "ProductSearchDocuments");

            migrationBuilder.DropTable(
                name: "ProductTags");

            migrationBuilder.DropTable(
                name: "ProductWorks");

            migrationBuilder.DropTable(
                name: "SystemAlerts");

            migrationBuilder.DropTable(
                name: "SystemState");

            migrationBuilder.DropTable(
                name: "TemporaryUploads");

            migrationBuilder.DropTable(
                name: "WorkSeries");

            migrationBuilder.DropTable(
                name: "InventoryItems");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "ImageAssets");

            migrationBuilder.DropTable(
                name: "Series");

            migrationBuilder.DropTable(
                name: "Works");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "ImageFiles");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "Collections");
        }
    }
}
