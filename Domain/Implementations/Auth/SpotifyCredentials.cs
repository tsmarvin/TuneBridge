using System.Text;

namespace TuneBridge.Domain.Implementations.Auth {

    /// <summary>
    /// Encapsulates Spotify API credentials (Client ID and Client Secret) and automatically encodes them
    /// in the Base64 format required for OAuth 2.0 Basic authentication. This class is immutable and
    /// thread-safe, designed to be registered as a singleton in dependency injection.
    /// </summary>
    /// <param name="clientId">
    /// The Client ID from your Spotify for Developers app. This is a public identifier safe to log
    /// and expose in non-production environments. Typically a 32-character hexadecimal string.
    /// </param>
    /// <param name="clientSecret">
    /// The Client Secret from your Spotify for Developers app. This is a sensitive credential that
    /// must be kept confidential. Should be stored in environment variables or secure configuration,
    /// never committed to source control.
    /// </param>
    /// <remarks>
    /// Credentials are obtained by creating an app in the Spotify Developer Dashboard:
    /// https://developer.spotify.com/dashboard/applications
    /// The encoded credentials are used in the Authorization header when requesting OAuth tokens.
    /// </remarks>
    public sealed class SpotifyCredentials(
        string clientId,
        string clientSecret
    ) {
        /// <summary>
        /// The Base64-encoded representation of "clientId:clientSecret", ready for use in HTTP Basic
        /// authentication headers. This is the format required by the Spotify token endpoint per
        /// OAuth 2.0 client credentials specification (RFC 6749, Section 2.3.1).
        /// </summary>
        /// <example>
        /// For clientId "abc123" and clientSecret "xyz789", this would contain the Base64 encoding
        /// of "abc123:xyz789", which can be used as: Authorization: Basic {Credentials}
        /// </example>
        public string Credentials { get; }
            = Convert.ToBase64String( Encoding.UTF8.GetBytes( $"{clientId}:{clientSecret}" ) );
    }

}
