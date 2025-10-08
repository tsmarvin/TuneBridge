using System.Text;

namespace TuneBridge.Domain.Implementations.Auth {

    /// <summary>
    /// Holds Spotify API credentials and provides them in Base64-encoded format for Basic authentication.
    /// </summary>
    /// <param name="clientId">The Spotify API client ID.</param>
    /// <param name="clientSecret">The Spotify API client secret.</param>
    public sealed class SpotifyCredentials(
        string clientId,
        string clientSecret
    ) {
        /// <summary>
        /// The Base64-encoded credentials in the format "clientId:clientSecret".
        /// </summary>
        public string Credentials { get; }
            = Convert.ToBase64String( Encoding.UTF8.GetBytes( $"{clientId}:{clientSecret}" ) );
    }

}
