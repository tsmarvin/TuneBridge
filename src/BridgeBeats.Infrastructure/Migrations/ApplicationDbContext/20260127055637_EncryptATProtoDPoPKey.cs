using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BridgeBeats.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// This migration adds a new column intended for storing ATProto DPoP keys.
    /// IMPORTANT: Despite the [ProtectedPersonalData] attribute on the property, automatic encryption
    /// is NOT currently functional because IPersonalDataProtector is not properly configured.
    /// The column name "Encrypted" is aspirational - data will be stored as plain text until
    /// proper Data Protection / IPersonalDataProtector infrastructure is implemented.
    /// 
    /// Any users with existing OAuth sessions will need to re-authenticate after this migration
    /// is applied. This is intentional, as previously stored keys were unencrypted and are not
    /// migrated to the new column.
    /// 
    /// TODO: Implement IPersonalDataProtector or use AddIdentity() instead of AddIdentityCore()
    /// to enable automatic encryption/decryption of properties with [ProtectedPersonalData] attribute.
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
