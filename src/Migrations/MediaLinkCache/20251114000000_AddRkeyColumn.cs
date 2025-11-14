using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TuneBridge.Migrations.MediaLinkCache {
    /// <inheritdoc />
    public partial class AddRkeyColumn : Migration {
        /// <inheritdoc />
        protected override void Up( MigrationBuilder migrationBuilder ) {
            // Add Rkey column to CacheEntries table
            _ = migrationBuilder.AddColumn<string>(
                name: "Rkey",
                table: "CacheEntries",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: ""
            );

            // Create unique index on Rkey
            _ = migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_Rkey",
                table: "CacheEntries",
                column: "Rkey",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down( MigrationBuilder migrationBuilder ) {
            // Drop the Rkey index
            _ = migrationBuilder.DropIndex(
                name: "IX_CacheEntries_Rkey",
                table: "CacheEntries"
            );

            // Drop the Rkey column
            _ = migrationBuilder.DropColumn(
                name: "Rkey",
                table: "CacheEntries"
            );
        }
    }
}
