using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoodsManager.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class ImageFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "ImageSettings",
                columns: table => new
                {
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
                    table.PrimaryKey("PK_ImageSettings", x => x.Id);
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
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageAssets", x => x.ImageAssetId);
                    table.CheckConstraint("CK_ImageAsset_Original", "(KeepOriginal=1 AND OriginalFileId IS NOT NULL) OR (KeepOriginal=0 AND OriginalFileId IS NULL)");
                    table.CheckConstraint("CK_ImageAsset_PolicyJson", "json_valid(PolicyJson)");
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
                name: "InventoryItemImages",
                columns: table => new
                {
                    InventoryItemImageId = table.Column<string>(type: "TEXT", nullable: false),
                    InventoryItemId = table.Column<string>(type: "TEXT", nullable: false),
                    ImageAssetId = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItemImages", x => x.InventoryItemImageId);
                    table.CheckConstraint("CK_InventoryItemImages_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_InventoryItemImages_Id_Storage", "typeof(\"InventoryItemImageId\") = 'text' AND length(\"InventoryItemImageId\") = 36 AND instr(\"InventoryItemImageId\", char(0)) = 0 AND substr(\"InventoryItemImageId\",9,1) = '-' AND substr(\"InventoryItemImageId\",14,1) = '-' AND substr(\"InventoryItemImageId\",19,1) = '-' AND substr(\"InventoryItemImageId\",24,1) = '-' AND length(replace(\"InventoryItemImageId\",'-','')) = 32 AND replace(\"InventoryItemImageId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_InventoryItemImages_ImageAssetId_Storage", "typeof(\"ImageAssetId\") = 'text' AND length(\"ImageAssetId\") = 36 AND instr(\"ImageAssetId\", char(0)) = 0 AND substr(\"ImageAssetId\",9,1) = '-' AND substr(\"ImageAssetId\",14,1) = '-' AND substr(\"ImageAssetId\",19,1) = '-' AND substr(\"ImageAssetId\",24,1) = '-' AND length(replace(\"ImageAssetId\",'-','')) = 32 AND replace(\"ImageAssetId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_InventoryItemImages_InventoryItemId_Storage", "typeof(\"InventoryItemId\") = 'text' AND length(\"InventoryItemId\") = 36 AND instr(\"InventoryItemId\", char(0)) = 0 AND substr(\"InventoryItemId\",9,1) = '-' AND substr(\"InventoryItemId\",14,1) = '-' AND substr(\"InventoryItemId\",19,1) = '-' AND substr(\"InventoryItemId\",24,1) = '-' AND length(replace(\"InventoryItemId\",'-','')) = 32 AND replace(\"InventoryItemId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_InventoryItemImages_SortOrder_Storage", "typeof(\"SortOrder\") = 'integer'");
                    table.CheckConstraint("CK_InventoryItemImages_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ItemImage_Sort", "SortOrder>=0");
                    table.ForeignKey(
                        name: "FK_InventoryItemImages_ImageAssets_ImageAssetId",
                        column: x => x.ImageAssetId,
                        principalTable: "ImageAssets",
                        principalColumn: "ImageAssetId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryItemImages_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "InventoryItemId",
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
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.ProductImageId);
                    table.CheckConstraint("CK_ProductImage_Sort", "SortOrder>=0");
                    table.CheckConstraint("CK_ProductImages_CreatedAt_Storage", "typeof(\"CreatedAt\") = 'integer' AND \"CreatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.CheckConstraint("CK_ProductImages_Id_Storage", "typeof(\"ProductImageId\") = 'text' AND length(\"ProductImageId\") = 36 AND instr(\"ProductImageId\", char(0)) = 0 AND substr(\"ProductImageId\",9,1) = '-' AND substr(\"ProductImageId\",14,1) = '-' AND substr(\"ProductImageId\",19,1) = '-' AND substr(\"ProductImageId\",24,1) = '-' AND length(replace(\"ProductImageId\",'-','')) = 32 AND replace(\"ProductImageId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductImages_ImageAssetId_Storage", "typeof(\"ImageAssetId\") = 'text' AND length(\"ImageAssetId\") = 36 AND instr(\"ImageAssetId\", char(0)) = 0 AND substr(\"ImageAssetId\",9,1) = '-' AND substr(\"ImageAssetId\",14,1) = '-' AND substr(\"ImageAssetId\",19,1) = '-' AND substr(\"ImageAssetId\",24,1) = '-' AND length(replace(\"ImageAssetId\",'-','')) = 32 AND replace(\"ImageAssetId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductImages_IsPrimary_Storage", "typeof(\"IsPrimary\") = 'integer' AND \"IsPrimary\" IN (0,1)");
                    table.CheckConstraint("CK_ProductImages_ProductId_Storage", "typeof(\"ProductId\") = 'text' AND length(\"ProductId\") = 36 AND instr(\"ProductId\", char(0)) = 0 AND substr(\"ProductId\",9,1) = '-' AND substr(\"ProductId\",14,1) = '-' AND substr(\"ProductId\",19,1) = '-' AND substr(\"ProductId\",24,1) = '-' AND length(replace(\"ProductId\",'-','')) = 32 AND replace(\"ProductId\",'-','') NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_ProductImages_SortOrder_Storage", "typeof(\"SortOrder\") = 'integer'");
                    table.CheckConstraint("CK_ProductImages_UpdatedAt_Storage", "typeof(\"UpdatedAt\") = 'integer' AND \"UpdatedAt\" BETWEEN -62135596800000 AND 253402300799999");
                    table.ForeignKey(
                        name: "FK_ProductImages_ImageAssets_ImageAssetId",
                        column: x => x.ImageAssetId,
                        principalTable: "ImageAssets",
                        principalColumn: "ImageAssetId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
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
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryUploads", x => x.TemporaryUploadId);
                    table.CheckConstraint("CK_TemporaryUploads_ByteLength_Storage", "typeof(\"ByteLength\") = 'integer'");
                    table.CheckConstraint("CK_TemporaryUploads_ClientUploadKey_Storage", "typeof(\"ClientUploadKey\") = 'text' AND length(\"ClientUploadKey\") = 36 AND instr(\"ClientUploadKey\", char(0)) = 0 AND substr(\"ClientUploadKey\",9,1) = '-' AND substr(\"ClientUploadKey\",14,1) = '-' AND substr(\"ClientUploadKey\",19,1) = '-' AND substr(\"ClientUploadKey\",24,1) = '-' AND length(replace(\"ClientUploadKey\",'-','')) = 32 AND replace(\"ClientUploadKey\",'-','') NOT GLOB '*[^0-9a-f]*'");
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
                        name: "FK_TemporaryUploads_ImageAssets_ImageAssetId",
                        column: x => x.ImageAssetId,
                        principalTable: "ImageAssets",
                        principalColumn: "ImageAssetId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_DisplayFileId",
                table: "ImageAssets",
                column: "DisplayFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_OriginalFileId",
                table: "ImageAssets",
                column: "OriginalFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_SourceSha256_PolicyHash",
                table: "ImageAssets",
                columns: new[] { "SourceSha256", "PolicyHash" },
                unique: true);

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
                name: "IX_InventoryItemImages_ImageAssetId",
                table: "InventoryItemImages",
                column: "ImageAssetId");

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
                name: "IX_ProductImages_ImageAssetId",
                table: "ProductImages",
                column: "ImageAssetId");

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
                name: "IX_SystemAlerts_AlertKey",
                table: "SystemAlerts",
                column: "AlertKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_ResolvedAt",
                table: "SystemAlerts",
                column: "ResolvedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryUploads_ImageAssetId",
                table: "TemporaryUploads",
                column: "ImageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryUploads_ReplayUntil",
                table: "TemporaryUploads",
                column: "ReplayUntil");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryUploads_State_ExpiresAt",
                table: "TemporaryUploads",
                columns: new[] { "State", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryUploads_UserId_ClientUploadKey",
                table: "TemporaryUploads",
                columns: new[] { "UserId", "ClientUploadKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImageSettings");

            migrationBuilder.DropTable(
                name: "InventoryItemImages");

            migrationBuilder.DropTable(
                name: "ProductImages");

            migrationBuilder.DropTable(
                name: "SystemAlerts");

            migrationBuilder.DropTable(
                name: "TemporaryUploads");

            migrationBuilder.DropTable(
                name: "ImageAssets");

            migrationBuilder.DropTable(
                name: "ImageFiles");
        }
    }
}
