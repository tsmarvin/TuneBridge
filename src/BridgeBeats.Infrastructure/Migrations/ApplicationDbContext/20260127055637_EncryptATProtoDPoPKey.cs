using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BridgeBeats.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EncryptATProtoDPoPKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptedAtProtoDPoPKey",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedAtProtoDPoPKey",
                table: "AspNetUsers");
        }
    }
}
