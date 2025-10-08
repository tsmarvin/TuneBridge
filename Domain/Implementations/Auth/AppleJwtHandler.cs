using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TuneBridge.Domain.Implementations.Auth {

    /// <summary>
    /// Generates and signs JSON Web Tokens (JWT) for authenticating with Apple's MusicKit API. Converts
    /// ES256 private keys (.p8 files) into signing credentials and creates short-lived tokens on demand.
    /// Unlike OAuth, Apple MusicKit uses stateless JWT authentication where tokens are created client-side.
    /// </summary>
    /// <remarks>
    /// Setup requires a private key (.p8 file) generated in the Apple Developer portal along with the
    /// Team ID and Key ID. Tokens are valid for 24 hours but are generated fresh for each request rather
    /// than cached, which simplifies implementation at minimal performance cost.
    /// See: https://developer.apple.com/documentation/applemusicapi/generating_developer_tokens
    /// </remarks>
    public class AppleJwtHandler {

        /// <summary>
        /// Initializes the JWT handler with Apple MusicKit credentials and prepares the ES256 signing
        /// infrastructure. The private key is imported and kept in memory for generating tokens on demand.
        /// </summary>
        /// <param name="teamId">
        /// Your Apple Developer Team ID (10-character alphanumeric). Found in the Apple Developer portal
        /// under Membership details. Acts as the JWT issuer claim.
        /// </param>
        /// <param name="keyId">
        /// The 10-character identifier for your MusicKit private key. Displayed when creating the key
        /// in the Apple Developer portal. Embedded in the JWT header as the "kid" (key ID) claim.
        /// </param>
        /// <param name="keyContents">
        /// The complete text content of the .p8 private key file, including the BEGIN/END PRIVATE KEY
        /// markers. The key uses ES256 (ECDSA with P-256 curve and SHA-256) and must match the keyId.
        /// </param>
        /// <exception cref="CryptographicException">
        /// Thrown if the keyContents cannot be parsed as a valid PEM-encoded ECDSA private key.
        /// </exception>
        public AppleJwtHandler(
            string teamId,
            string keyId,
            string keyContents
        ) {
            ECDsa algo = ECDsa.Create();
            algo.ImportFromPem( keyContents );
            _signingCreds = new SigningCredentials(
                key: new ECDsaSecurityKey( algo ) { KeyId = keyId },
                algorithm: SecurityAlgorithms.EcdsaSha256
            );
            _teamId = teamId;
        }

        private readonly string _teamId;
        private readonly JsonWebTokenHandler _handler = new();
        private readonly SigningCredentials _signingCreds;

        /// <summary>
        /// Generates a new signed JWT token for MusicKit API authentication and returns it in a Bearer
        /// authorization header. Each call creates a fresh token valid for 24 hours. Apple recommends
        /// creating tokens on demand rather than caching them.
        /// </summary>
        /// <returns>
        /// An <see cref="AuthenticationHeaderValue"/> ready to be assigned to HttpClient authorization.
        /// The token includes the Team ID as issuer, current timestamp, and 24-hour expiration.
        /// </returns>
        /// <remarks>
        /// Token structure follows Apple's MusicKit requirements: ES256 algorithm, team ID as issuer,
        /// key ID in header. No audience or subject claims needed. Tokens are stateless and can't be
        /// revoked before expiration.
        /// </remarks>
        public AuthenticationHeaderValue NewAuthenticationHeader( ) {
            string token = _handler.CreateToken(new SecurityTokenDescriptor {
                Issuer = _teamId,
                IssuedAt = DateTime.Now.ToUniversalTime(),
                Expires = DateTime.Now.AddDays(1).ToUniversalTime(),
                SigningCredentials = _signingCreds
            });
            return new AuthenticationHeaderValue( "Bearer", token );
        }
    }

}
