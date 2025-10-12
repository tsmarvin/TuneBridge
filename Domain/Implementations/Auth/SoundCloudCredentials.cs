using System.Text;

namespace TuneBridge.Domain.Implementations.Auth {

    /// <summary>
    /// Encapsulates SoundCloud API credentials for OAuth 2.1 client credentials flow authentication.
    /// This class is immutable and thread-safe, designed to be registered as a singleton in dependency injection.
    /// </summary>
    /// <param name="clientId">
    /// The Client ID from your SoundCloud app. Used in the OAuth token request.
    /// </param>
    /// <param name="clientSecret">
    /// The Client Secret from your SoundCloud app. Used in the OAuth token request.
    /// </param>
    /// <remarks>
    /// Credentials are obtained by requesting API access via SoundCloud support.
    /// See: https://help.soundcloud.com/hc/en-us/requests/new
    /// SoundCloud uses OAuth 2.1 with client credentials flow.
    /// See: https://developers.soundcloud.com/docs/api/guide#authentication
    /// </remarks>
    public sealed class SoundCloudCredentials(
        string clientId,
        string clientSecret
    ) {
        /// <summary>
        /// The Client ID for SoundCloud OAuth authentication.
        /// </summary>
        public string ClientId { get; } = clientId;

        /// <summary>
        /// The Client Secret for SoundCloud OAuth authentication.
        /// </summary>
        public string ClientSecret { get; } = clientSecret;

        /// <summary>
        /// The Base64-encoded representation of "clientId:clientSecret" for HTTP Basic authentication
        /// used in the OAuth token request.
        /// </summary>
        public string Credentials { get; }
            = Convert.ToBase64String( Encoding.UTF8.GetBytes( $"{clientId}:{clientSecret}" ) );
    }

}
