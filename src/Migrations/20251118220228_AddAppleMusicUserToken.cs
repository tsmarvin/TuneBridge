using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TuneBridge.Migrations
{
    /// <inheritdoc />
    public partial class AddAppleMusicUserToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AppleMusicTokenExpiration",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppleMusicUserToken",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppleMusicTokenExpiration",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AppleMusicUserToken",
                table: "AspNetUsers");
        }
    }
}
