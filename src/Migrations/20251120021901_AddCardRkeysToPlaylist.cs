using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TuneBridge.Migrations {
    /// <inheritdoc />
    public partial class AddCardRkeysToPlaylist : Migration {
        /// <inheritdoc />
        protected override void Up( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.AddColumn<string>(
                name: "CardRkeys",
                table: "Playlists",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "" );
        }

        /// <inheritdoc />
        protected override void Down( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.DropColumn(
                name: "CardRkeys",
                table: "Playlists" );
        }
    }
}
