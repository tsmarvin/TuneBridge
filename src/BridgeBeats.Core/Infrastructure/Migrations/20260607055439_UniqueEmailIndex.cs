using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BridgeBeats.Core.Infrastructure.Migrations {
    /// <inheritdoc />
    public partial class UniqueEmailIndex : Migration {
        /// <inheritdoc />
        protected override void Up( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers" );

            _ = migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true );
        }

        /// <inheritdoc />
        protected override void Down( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers" );

            _ = migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail" );
        }
    }
}
