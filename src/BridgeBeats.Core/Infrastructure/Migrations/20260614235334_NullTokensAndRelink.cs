using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BridgeBeats.Core.Infrastructure.Migrations {
    /// <summary>
    /// Nulls the four credential token columns and their expiration timestamps for every user row so
    /// that no pre-existing plaintext token values remain after the EF value converter starts encrypting
    /// new writes. Affected users will receive a 401 the next time they try to use an Apple Music or
    /// ATProto feature and must re-link via the OAuth / Apple Music connect flows, which writes a fresh
    /// encrypted token on completion.
    /// </summary>
    public partial class NullTokensAndRelink : Migration {
        /// <inheritdoc />
        protected override void Up( MigrationBuilder migrationBuilder ) {
            // Null every credential token column and its expiration for all existing users.
            // The EF value converter installed in ApplicationDbContext.OnModelCreating will write
            // ciphertext on all subsequent updates, so no plaintext tokens remain after this migration.
            _ = migrationBuilder.Sql(
                """
                UPDATE AspNetUsers
                SET AppleMusicUserToken = NULL,
                    AppleMusicTokenExpiration = NULL,
                    AtProtoAccessToken = NULL,
                    AtProtoRefreshToken = NULL,
                    AtProtoDPoPKey = NULL,
                    AtProtoTokenExpiration = NULL;
                """ );
        }

        /// <inheritdoc />
        protected override void Down( MigrationBuilder migrationBuilder ) {
            // Up nulls token columns irreversibly; affected users re-link via the OAuth /
            // Apple Music connect flows. There is no data to restore.
        }
    }
}
