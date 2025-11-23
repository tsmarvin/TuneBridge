using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TuneBridge.Migrations {
    /// <inheritdoc />
    public partial class AddPlaylistsTable : Migration {
        /// <inheritdoc />
        protected override void Up( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new {
                    PlaylistId = table.Column<string>( type: "TEXT", maxLength: 64, nullable: false ),
                    UserId = table.Column<string>( type: "TEXT", maxLength: 450, nullable: true ),
                    Title = table.Column<string>( type: "TEXT", maxLength: 200, nullable: true ),
                    Description = table.Column<string>( type: "TEXT", maxLength: 1000, nullable: true ),
                    CardIds = table.Column<string>( type: "TEXT", maxLength: 2000, nullable: false ),
                    CreatedAt = table.Column<DateTime>( type: "TEXT", nullable: false ),
                    ExpiresAt = table.Column<DateTime>( type: "TEXT", nullable: true )
                },
                constraints: table => {
                    _ = table.PrimaryKey( "PK_Playlists", x => x.PlaylistId );
                } );

            _ = migrationBuilder.CreateIndex(
                name: "IX_Playlists_CreatedAt",
                table: "Playlists",
                column: "CreatedAt" );

            _ = migrationBuilder.CreateIndex(
                name: "IX_Playlists_ExpiresAt",
                table: "Playlists",
                column: "ExpiresAt" );

            _ = migrationBuilder.CreateIndex(
                name: "IX_Playlists_UserId",
                table: "Playlists",
                column: "UserId" );
        }

        /// <inheritdoc />
        protected override void Down( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.DropTable(
                name: "Playlists" );
        }
    }
}
