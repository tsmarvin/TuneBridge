using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TuneBridge.Migrations.MediaLinkCache
{
    /// <inheritdoc />
    public partial class MediaLinkCacheInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CacheEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecordUri = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLookedUpAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InputLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Link = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    MediaLinkCacheEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InputLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InputLinks_CacheEntries_MediaLinkCacheEntryId",
                        column: x => x.MediaLinkCacheEntryId,
                        principalTable: "CacheEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_InputLinks_Link",
                table: "InputLinks",
                column: "Link",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InputLinks_MediaLinkCacheEntryId",
                table: "InputLinks",
                column: "MediaLinkCacheEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InputLinks");

            migrationBuilder.DropTable(
                name: "CacheEntries");
        }
    }
}
