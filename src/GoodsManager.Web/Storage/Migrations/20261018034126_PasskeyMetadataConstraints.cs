using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoodsManager.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class PasskeyMetadataConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Passkey_CredentialId_Length",
                table: "AspNetUserPasskeys",
                sql: "length(CredentialId) BETWEEN 1 AND 1023");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Passkey_Name",
                table: "AspNetUserPasskeys",
                sql: "json_type(Data,'$.Name') IS 'text' AND length(json_extract(Data,'$.Name')) BETWEEN 1 AND 100 AND instr(json_extract(Data,'$.Name'),char(0))=0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Passkey_CredentialId_Length",
                table: "AspNetUserPasskeys");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Passkey_Name",
                table: "AspNetUserPasskeys");
        }
    }
}
