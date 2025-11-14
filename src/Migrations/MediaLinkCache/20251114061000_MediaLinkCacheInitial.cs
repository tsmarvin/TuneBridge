using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TuneBridge.Migrations.MediaLinkCache {
    /// <inheritdoc />
    public partial class MediaLinkCacheInitial : Migration {
        /// <inheritdoc />
        protected override void Up( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.CreateTable(
                name: "CacheEntries",
                columns: table => new {
                    Id = table.Column<int>( type: "INTEGER", nullable: false )
                        .Annotation( "Sqlite:Autoincrement", true ),
                    Rkey = table.Column<string>( type: "TEXT", maxLength: 200, nullable: false ),
                    RecordUri = table.Column<string>( type: "TEXT", maxLength: 500, nullable: false ),
                    CreatedAt = table.Column<DateTime>( type: "TEXT", nullable: false ),
                    LastLookedUpAt = table.Column<DateTime>( type: "TEXT", nullable: false )
                },
                constraints: table => {
                    _ = table.PrimaryKey( "PK_CacheEntries", x => x.Id );
                } );

            _ = migrationBuilder.CreateTable(
                name: "InputLinks",
                columns: table => new {
                    Id = table.Column<int>( type: "INTEGER", nullable: false )
                        .Annotation( "Sqlite:Autoincrement", true ),
                    Link = table.Column<string>( type: "TEXT", maxLength: 1000, nullable: false ),
                    MediaLinkCacheEntryId = table.Column<int>( type: "INTEGER", nullable: false ),
                    CreatedAt = table.Column<DateTime>( type: "TEXT", nullable: false )
                },
                constraints: table => {
                    _ = table.PrimaryKey( "PK_InputLinks", x => x.Id );
                    _ = table.ForeignKey(
                        name: "FK_InputLinks_CacheEntries_MediaLinkCacheEntryId",
                        column: x => x.MediaLinkCacheEntryId,
                        principalTable: "CacheEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                } );

            _ = migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_LastLookedUpAt",
                table: "CacheEntries",
                column: "LastLookedUpAt" );

            _ = migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_RecordUri",
                table: "CacheEntries",
                column: "RecordUri",
                unique: true
            );

            _ = migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_Rkey",
                table: "CacheEntries",
                column: "Rkey",
                unique: true
            );

            _ = migrationBuilder.CreateIndex(
                name: "IX_InputLinks_Link",
                table: "InputLinks",
                column: "Link",
                unique: true
            );

            _ = migrationBuilder.CreateIndex(
                name: "IX_InputLinks_MediaLinkCacheEntryId",
                table: "InputLinks",
                column: "MediaLinkCacheEntryId"
            );
        }

        /// <inheritdoc />
        protected override void Down( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.DropTable(
                name: "InputLinks"
            );

            _ = migrationBuilder.DropTable(
                name: "CacheEntries"
            );
        }
    }
}
