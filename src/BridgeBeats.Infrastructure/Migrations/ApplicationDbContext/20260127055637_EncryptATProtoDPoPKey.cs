using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BridgeBeats.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// This migration adds encrypted storage for DPoP keys but does not migrate existing data.
    /// Any users with existing OAuth sessions will need to re-authenticate after this migration
    /// is applied. This is intentional as existing keys were stored unencrypted and cannot be
    /// safely migrated to the new encrypted storage without the Data Protection API infrastructure.
    /// </remarks>
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
