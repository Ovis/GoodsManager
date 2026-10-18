using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoodsManager.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class IdentityPasswordAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Singleton = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.CheckConstraint("CK_AspNetUsers_LockoutEnabled_Storage", "typeof(\"LockoutEnabled\") = 'integer' AND \"LockoutEnabled\" IN (0,1)");
                    table.CheckConstraint("CK_AspNetUsers_LockoutEnd_Storage", "\"LockoutEnd\" IS NULL OR (typeof(\"LockoutEnd\") = 'integer' AND \"LockoutEnd\" BETWEEN -62135596800000 AND 253402300799999)");
                    table.CheckConstraint("CK_AspNetUsers_NormalizedEmail_Storage", "\"NormalizedEmail\" IS NULL OR (typeof(\"NormalizedEmail\") = 'text' AND instr(\"NormalizedEmail\", char(0)) = 0)");
                    table.CheckConstraint("CK_AspNetUsers_NormalizedUserName_Storage", "typeof(\"NormalizedUserName\") = 'text' AND instr(\"NormalizedUserName\", char(0)) = 0");
                    table.CheckConstraint("CK_AspNetUsers_PasswordHash_Storage", "typeof(\"PasswordHash\") = 'text' AND instr(\"PasswordHash\", char(0)) = 0");
                    table.CheckConstraint("CK_AspNetUsers_PhoneNumber_Storage", "\"PhoneNumber\" IS NULL OR (typeof(\"PhoneNumber\") = 'text' AND instr(\"PhoneNumber\", char(0)) = 0)");
                    table.CheckConstraint("CK_AspNetUsers_PhoneNumberConfirmed_Storage", "typeof(\"PhoneNumberConfirmed\") = 'integer' AND \"PhoneNumberConfirmed\" IN (0,1)");
                    table.CheckConstraint("CK_AspNetUsers_SecurityStamp_Storage", "typeof(\"SecurityStamp\") = 'text' AND instr(\"SecurityStamp\", char(0)) = 0");
                    table.CheckConstraint("CK_AspNetUsers_Singleton_Storage", "typeof(\"Singleton\") = 'integer'");
                    table.CheckConstraint("CK_AspNetUsers_TwoFactorEnabled_Storage", "typeof(\"TwoFactorEnabled\") = 'integer' AND \"TwoFactorEnabled\" IN (0,1)");
                    table.CheckConstraint("CK_AspNetUsers_UserName_Storage", "typeof(\"UserName\") = 'text' AND instr(\"UserName\", char(0)) = 0");
                    table.CheckConstraint("CK_User_Failures", "AccessFailedCount>=0");
                    table.CheckConstraint("CK_User_Name", "length(UserName) BETWEEN 1 AND 64 AND UserName NOT GLOB '*[^a-zA-Z0-9._-]*'");
                    table.CheckConstraint("CK_User_Singleton", "Singleton=1");
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
                    table.CheckConstraint("CK_Passkey_Data", "typeof(Data)='text' AND json_valid(Data)=1 AND json_type(Data)='object'");
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
                name: "IX_AspNetUsers_Singleton",
                table: "AspNetUsers",
                column: "Singleton",
                unique: true);

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
                name: "AspNetUsers");
        }
    }
}
