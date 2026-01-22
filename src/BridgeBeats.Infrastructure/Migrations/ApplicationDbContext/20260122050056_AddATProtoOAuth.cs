using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BridgeBeats.Infrastructure.Migrations {
    /// <inheritdoc />
    public partial class AddATProtoOAuth : Migration {
        /// <inheritdoc />
        protected override void Up( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.AddColumn<string>(
                name: "AtProtoAccessToken",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true );

            _ = migrationBuilder.AddColumn<string>(
                name: "AtProtoDPoPKey",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true );

            _ = migrationBuilder.AddColumn<string>(
                name: "AtProtoDid",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true );

            _ = migrationBuilder.AddColumn<string>(
                name: "AtProtoHandle",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true );

            _ = migrationBuilder.AddColumn<string>(
                name: "AtProtoRefreshToken",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true );

            _ = migrationBuilder.AddColumn<DateTime>(
                name: "AtProtoTokenExpiration",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true );

            _ = migrationBuilder.CreateTable(
                name: "AtProtoOAuthStates",
                columns: table => new {
                    State = table.Column<string>( type: "TEXT", maxLength: 128, nullable: false ),
                    CodeVerifier = table.Column<string>( type: "TEXT", maxLength: 128, nullable: false ),
                    Handle = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                    Did = table.Column<string>( type: "TEXT", maxLength: 128, nullable: true ),
                    PdsUri = table.Column<string>( type: "TEXT", maxLength: 512, nullable: true ),
                    AuthorizationServerUri = table.Column<string>( type: "TEXT", maxLength: 512, nullable: true ),
                    DPoPKeyJwk = table.Column<string>( type: "TEXT", nullable: false ),
                    CreatedAt = table.Column<DateTime>( type: "TEXT", nullable: false ),
                    ExpiresAt = table.Column<DateTime>( type: "TEXT", nullable: false )
                },
                constraints: table => {
                    _ = table.PrimaryKey( "PK_AtProtoOAuthStates", x => x.State );
                } );

            _ = migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_AtProtoDid",
                table: "AspNetUsers",
                column: "AtProtoDid",
                unique: true,
                filter: "[AtProtoDid] IS NOT NULL" );

            _ = migrationBuilder.CreateIndex(
                name: "IX_AtProtoOAuthStates_ExpiresAt",
                table: "AtProtoOAuthStates",
                column: "ExpiresAt" );
        }

        /// <inheritdoc />
        protected override void Down( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.DropTable(
                name: "AtProtoOAuthStates" );

            _ = migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_AtProtoDid",
                table: "AspNetUsers" );

            _ = migrationBuilder.DropColumn(
                name: "AtProtoAccessToken",
                table: "AspNetUsers" );

            _ = migrationBuilder.DropColumn(
                name: "AtProtoDPoPKey",
                table: "AspNetUsers" );

            _ = migrationBuilder.DropColumn(
                name: "AtProtoDid",
                table: "AspNetUsers" );

            _ = migrationBuilder.DropColumn(
                name: "AtProtoHandle",
                table: "AspNetUsers" );

            _ = migrationBuilder.DropColumn(
                name: "AtProtoRefreshToken",
                table: "AspNetUsers" );

            _ = migrationBuilder.DropColumn(
                name: "AtProtoTokenExpiration",
                table: "AspNetUsers" );
        }
    }
}
