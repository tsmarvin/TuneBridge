using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic {

    /// <summary>
    /// Mints the Apple developer token used to authenticate requests to the Apple Music API. Converts an
    /// ES256 private key (the <c>.p8</c> key issued by Apple) into signing credentials and creates
    /// short-lived tokens on demand.
    /// </summary>
    /// <remarks>
    /// The token is a JSON Web Token signed with the ES256 (ECDSA over the NIST P-256 curve with SHA-256)
    /// algorithm, as required by Apple. Signing is local; this type makes no network calls. One instance
    /// holds the signing credentials for the lifetime of the configured developer key and produces a fresh,
    /// short-lived token on each call to <see cref="NewAuthenticationHeader"/>. Setup requires a private
    /// key (.p8 file) generated in the Apple Developer portal along with the Team ID and Key ID.
    /// See: https://developer.apple.com/documentation/applemusicapi/generating_developer_tokens
    /// </remarks>
    public class AppleJwtHandler {

        /// <summary>
        /// Initializes a new <see cref="AppleJwtHandler"/> from an Apple developer signing key. The private
        /// key is imported and kept in memory for generating tokens on demand.
        /// </summary>
        /// <param name="teamId">
        /// The Apple developer team identifier, used as the JWT issuer (<c>iss</c>) claim.
        /// </param>
        /// <param name="keyId">
        /// The identifier of the private key, embedded as the <c>kid</c> header so Apple can select the
        /// matching public key for verification.
        /// </param>
        /// <param name="keyContents">
        /// The PEM-encoded contents of the ECDSA private key (the <c>.p8</c> key issued by Apple), including
        /// the BEGIN/END PRIVATE KEY markers. The key uses ES256 and must match the keyId.
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

        /// <summary>The Apple developer team identifier used as the token issuer claim.</summary>
        private readonly string _teamId;

        /// <summary>Handler that builds and signs the JSON Web Token.</summary>
        private readonly JsonWebTokenHandler _handler = new();

        /// <summary>The ES256 signing credentials derived from the configured developer key.</summary>
        private readonly SigningCredentials _signingCreds;

        /// <summary>
        /// Creates a freshly signed bearer authentication header for an Apple Music API request. Each call
        /// creates a fresh token valid for 24 hours; Apple recommends creating tokens on demand rather than
        /// caching them.
        /// </summary>
        /// <returns>
        /// An <see cref="AuthenticationHeaderValue"/> with the <c>Bearer</c> scheme carrying a newly minted
        /// JWT. The token is issued at the current UTC time and expires one day later, and carries the Team
        /// ID as issuer and the key ID in the header. The token is stateless and cannot be revoked before
        /// it expires.
        /// </returns>
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
