using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TuneBridge.Domain.Implementations.Auth {

    /// <summary>
    /// Converts the private key to the signing credentials used to create a new JWT token for Apple Music API requests. <para/>
    /// See <seealso href="https://developer.apple.com/help/account/configure-app-capabilities/create-a-media-identifier-and-private-key/">this doc</seealso>
    /// for more information on configuring the media identifier and generating a private key.
    /// </summary>
    /// <description>See <seealso href="https://developer.apple.com/help/account/configure-app-capabilities/create-a-media-identifier-and-private-key/">this doc</seealso></description>
    /// <param name="teamId">The apple teamId associated with the media identifier and private key.</param>
    /// <param name="keyId"></param>
    /// <param name="keyContents"></param>
    public class AppleJwtHandler {

        /// <summary>
        /// Creates a new instance of the <see cref="AppleJwtHandler"/> class, which can generate JWT tokens for Apple Music API requests.
        /// </summary>
        /// <param name="teamId">The apple teamId associated with the media identifier and private key.</param>
        /// <param name="keyId">The id associated with the private key file.</param>
        /// <param name="keyContents">The text contents of the private key file.</param>
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
