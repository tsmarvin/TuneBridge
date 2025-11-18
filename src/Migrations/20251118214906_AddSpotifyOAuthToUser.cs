using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TuneBridge.Migrations
{
    /// <inheritdoc />
    public partial class AddSpotifyOAuthToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpotifyAccessToken",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpotifyRefreshToken",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SpotifyTokenExpiry",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpotifyAccessToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SpotifyRefreshToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SpotifyTokenExpiry",
                table: "AspNetUsers");
        }
    }
}
