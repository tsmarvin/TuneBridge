using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TuneBridge.Migrations
{
    /// <inheritdoc />
    public partial class AddTidalTokensToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TidalAccessToken",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TidalRefreshToken",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TidalTokenExpiry",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TidalAccessToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TidalRefreshToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TidalTokenExpiry",
                table: "AspNetUsers");
        }
    }
}
