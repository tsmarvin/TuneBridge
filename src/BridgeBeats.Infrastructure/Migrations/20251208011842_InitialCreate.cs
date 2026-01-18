using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BridgeBeats.Infrastructure.Migrations {
    /// <inheritdoc />
    public partial class InitialCreate : Migration {
        /// <inheritdoc />
        protected override void Up( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.CreateTable(
                name: "CacheEntries",
                columns: table => new {
                    Rkey = table.Column<string>( type: "TEXT", maxLength: 200, nullable: false ),
                    CardId = table.Column<string>( type: "TEXT", maxLength: 64, nullable: false ),
                    RecordUri = table.Column<string>( type: "TEXT", maxLength: 500, nullable: false ),
                    CreatedAt = table.Column<DateTime>( type: "TEXT", nullable: false ),
                    LastLookedUpAt = table.Column<DateTime>( type: "TEXT", nullable: false )
                },
                constraints: table => {
                    _ = table.PrimaryKey( "PK_CacheEntries", x => x.Rkey );
                } );

            _ = migrationBuilder.CreateTable(
                name: "LookupEntries",
                columns: table => new {
                    Id = table.Column<int>( type: "INTEGER", nullable: false )
                        .Annotation( "Sqlite:Autoincrement", true ),
                    LookupValue = table.Column<string>( type: "TEXT", maxLength: 1000, nullable: false ),
                    LookupType = table.Column<int>( type: "INTEGER", nullable: false ),
                    IsAlbum = table.Column<bool>( type: "INTEGER", nullable: false ),
                    MediaLinkCacheEntryRkey = table.Column<string>( type: "TEXT", maxLength: 200, nullable: false ),
                    CreatedAt = table.Column<DateTime>( type: "TEXT", nullable: false )
                },
                constraints: table => {
                    _ = table.PrimaryKey( "PK_LookupEntries", x => x.Id );
                    _ = table.ForeignKey(
                        name: "FK_LookupEntries_CacheEntries_MediaLinkCacheEntryRkey",
                        column: x => x.MediaLinkCacheEntryRkey,
                        principalTable: "CacheEntries",
                        principalColumn: "Rkey",
                        onDelete: ReferentialAction.Cascade );
                } );

            _ = migrationBuilder.CreateTable(
                name: "ProviderEntries",
                columns: table => new {
                    Id = table.Column<int>( type: "INTEGER", nullable: false )
                        .Annotation( "Sqlite:Autoincrement", true ),
                    Provider = table.Column<int>( type: "INTEGER", nullable: false ),
                    ProviderId = table.Column<string>( type: "TEXT", maxLength: 200, nullable: false ),
                    MediaLinkCacheEntryRkey = table.Column<string>( type: "TEXT", maxLength: 200, nullable: false ),
                    CreatedAt = table.Column<DateTime>( type: "TEXT", nullable: false )
                },
                constraints: table => {
                    _ = table.PrimaryKey( "PK_ProviderEntries", x => x.Id );
                    _ = table.ForeignKey(
                        name: "FK_ProviderEntries_CacheEntries_MediaLinkCacheEntryRkey",
                        column: x => x.MediaLinkCacheEntryRkey,
                        principalTable: "CacheEntries",
                        principalColumn: "Rkey",
                        onDelete: ReferentialAction.Cascade );
                } );

            _ = migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_CardId",
                table: "CacheEntries",
                column: "CardId" );

            _ = migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_LastLookedUpAt",
                table: "CacheEntries",
                column: "LastLookedUpAt" );

            _ = migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_RecordUri",
                table: "CacheEntries",
                column: "RecordUri",
                unique: true );

            _ = migrationBuilder.CreateIndex(
                name: "IX_LookupEntries_LookupValue_LookupType_IsAlbum",
                table: "LookupEntries",
                columns: new[] { "LookupValue", "LookupType", "IsAlbum" },
                unique: true );

            _ = migrationBuilder.CreateIndex(
                name: "IX_LookupEntries_MediaLinkCacheEntryRkey",
                table: "LookupEntries",
                column: "MediaLinkCacheEntryRkey" );

            _ = migrationBuilder.CreateIndex(
                name: "IX_ProviderEntries_MediaLinkCacheEntryRkey",
                table: "ProviderEntries",
                column: "MediaLinkCacheEntryRkey" );

            _ = migrationBuilder.CreateIndex(
                name: "IX_ProviderEntries_Provider_ProviderId",
                table: "ProviderEntries",
                columns: new[] { "Provider", "ProviderId" },
                unique: true );

            _ = migrationBuilder.CreateIndex(
                name: "IX_ProviderEntries_ProviderId",
                table: "ProviderEntries",
                column: "ProviderId" );
        }

        /// <inheritdoc />
        protected override void Down( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.DropTable(
                name: "LookupEntries" );

            _ = migrationBuilder.DropTable(
                name: "ProviderEntries" );

            _ = migrationBuilder.DropTable(
                name: "CacheEntries" );
        }
    }
}
