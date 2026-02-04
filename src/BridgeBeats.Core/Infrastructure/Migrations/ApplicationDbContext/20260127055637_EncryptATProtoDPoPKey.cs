using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BridgeBeats.Infrastructure.Migrations {
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// This migration adds a column for ATProto DPoP private keys.
    /// </para>
    /// <para>
    /// <strong>SECURITY WARNING:</strong> Despite the column name "EncryptedAtProtoDPoPKey" and the
    /// [ProtectedPersonalData] attribute, data is currently stored as PLAIN TEXT. Automatic encryption
    /// requires implementing and registering IPersonalDataProtector, which is not yet done. The column
    /// name is misleading and will be addressed in a future update.
    /// </para>
    /// <para>
    /// Storing DPoP private keys in plain text is a security risk. These keys prove ownership of OAuth
    /// tokens, and database access could allow an attacker to impersonate users. This is a known issue
    /// and should be resolved before production deployment.
    /// </para>
    /// <para>
    /// Any users with existing OAuth sessions will need to re-authenticate after this migration
    /// is applied, as existing keys from the old column are not migrated.
    /// </para>
    /// </remarks>
    public partial class EncryptATProtoDPoPKey : Migration {
        /// <inheritdoc />
        protected override void Up( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.AddColumn<string>(
                name: "EncryptedAtProtoDPoPKey",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true );
        }

        /// <inheritdoc />
        protected override void Down( MigrationBuilder migrationBuilder ) {
            _ = migrationBuilder.DropColumn(
                name: "EncryptedAtProtoDPoPKey",
                table: "AspNetUsers" );
        }
    }
}
