using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TuneBridge.Migrations {
    /// <inheritdoc />
    public partial class AddAppleMusicUserToken : Migration {
        /// <inheritdoc />
        protected override void Up( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.AddColumn<DateTime>(
                name: "AppleMusicTokenExpiration",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true );

            _ = migrationBuilder.AddColumn<string>(
                name: "AppleMusicUserToken",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true );
        }

        /// <inheritdoc />
        protected override void Down( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.DropColumn(
                name: "AppleMusicTokenExpiration",
                table: "AspNetUsers" );

            _ = migrationBuilder.DropColumn(
                name: "AppleMusicUserToken",
                table: "AspNetUsers" );
        }
    }
}
