using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BridgeBeats.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnencryptedDPoPKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AtProtoDPoPKey",
                table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AtProtoDPoPKey",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }
    }
}
