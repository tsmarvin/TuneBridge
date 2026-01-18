using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BridgeBeats.Infrastructure.Migrations.MediaLinkCache
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CacheEntries",
                columns: table => new
                {
                    Rkey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CardId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RecordUri = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLookedUpAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheEntries", x => x.Rkey);
                });

            migrationBuilder.CreateTable(
                name: "LookupEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LookupValue = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    LookupType = table.Column<int>(type: "INTEGER", nullable: false),
                    IsAlbum = table.Column<bool>(type: "INTEGER", nullable: false),
                    MediaLinkCacheEntryRkey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LookupEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LookupEntries_CacheEntries_MediaLinkCacheEntryRkey",
                        column: x => x.MediaLinkCacheEntryRkey,
                        principalTable: "CacheEntries",
                        principalColumn: "Rkey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProviderEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    MediaLinkCacheEntryRkey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderEntries_CacheEntries_MediaLinkCacheEntryRkey",
                        column: x => x.MediaLinkCacheEntryRkey,
                        principalTable: "CacheEntries",
                        principalColumn: "Rkey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_CardId",
                table: "CacheEntries",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_LastLookedUpAt",
                table: "CacheEntries",
                column: "LastLookedUpAt");

            migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_RecordUri",
                table: "CacheEntries",
                column: "RecordUri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LookupEntries_LookupValue_LookupType_IsAlbum",
                table: "LookupEntries",
                columns: new[] { "LookupValue", "LookupType", "IsAlbum" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LookupEntries_MediaLinkCacheEntryRkey",
                table: "LookupEntries",
                column: "MediaLinkCacheEntryRkey");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEntries_MediaLinkCacheEntryRkey",
                table: "ProviderEntries",
                column: "MediaLinkCacheEntryRkey");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEntries_Provider_ProviderId",
                table: "ProviderEntries",
                columns: new[] { "Provider", "ProviderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEntries_ProviderId",
                table: "ProviderEntries",
                column: "ProviderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LookupEntries");

            migrationBuilder.DropTable(
                name: "ProviderEntries");

            migrationBuilder.DropTable(
                name: "CacheEntries");
        }
    }
}
