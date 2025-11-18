using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TuneBridge.JetstreamMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DetectedMusicLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PostUri = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    FirstDetectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SeenCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetectedMusicLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DetectedMusicLinks_FirstDetectedAt",
                table: "DetectedMusicLinks",
                column: "FirstDetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DetectedMusicLinks_Provider",
                table: "DetectedMusicLinks",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_DetectedMusicLinks_Url",
                table: "DetectedMusicLinks",
                column: "Url",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DetectedMusicLinks");
        }
    }
}
