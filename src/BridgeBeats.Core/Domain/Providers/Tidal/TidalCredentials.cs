using System.Text;

namespace BridgeBeats.Core.Domain.Providers.Tidal {

    /// <summary>
    /// Holds the Tidal OAuth2 client credentials precomputed as a base64
    /// HTTP Basic Authentication string.
    /// </summary>
    /// <remarks>
    /// The supplied client id and secret are joined as <c>clientId:clientSecret</c>,
    /// UTF-8 encoded, and base64-encoded at construction. The result is the value
    /// <see cref="TidalTokenHandler"/> sends in the <c>Basic</c> authorization header
    /// when requesting an application access token. The plain client id and secret are
    /// not retained.
    /// </remarks>
    /// <param name="clientId">The Tidal application client id.</param>
    /// <param name="clientSecret">The Tidal application client secret.</param>
    public sealed class TidalCredentials(
        string clientId,
        string clientSecret
    ) {
        /// <summary>
        /// Gets the base64-encoded <c>clientId:clientSecret</c> string used as the
        /// value of the HTTP Basic Authentication header on the Tidal token request.
        /// </summary>
        public string Credentials { get; }
            = Convert.ToBase64String( Encoding.UTF8.GetBytes( $"{clientId}:{clientSecret}" ) );
    }

}
