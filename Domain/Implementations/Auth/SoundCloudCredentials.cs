using System.Text;

namespace TuneBridge.Domain.Implementations.Auth {

    /// <summary>
    /// Encapsulates SoundCloud API credentials (Client ID and Client Secret) and automatically encodes them
    /// in the Base64 format required for OAuth 2.0 Basic authentication. This class is immutable and
    /// thread-safe, designed to be registered as a singleton in dependency injection.
    /// </summary>
    /// <param name="clientId">
    /// The Client ID from your SoundCloud app. This is a public identifier safe to log
    /// and expose in non-production environments.
    /// </param>
    /// <param name="clientSecret">
    /// The Client Secret from your SoundCloud app. This is a sensitive credential that
    /// must be kept confidential. Should be stored in environment variables or secure configuration,
    /// never committed to source control.
    /// </param>
    /// <remarks>
    /// Credentials are obtained by creating an app at: https://soundcloud.com/you/apps
    /// The encoded credentials are used in the Authorization header when requesting OAuth tokens.
    /// </remarks>
    public sealed class SoundCloudCredentials(
        string clientId,
        string clientSecret
    ) {
        /// <summary>
        /// The Client ID for SoundCloud API requests. SoundCloud API uses client_id as a query parameter.
        /// </summary>
        public string ClientId { get; } = clientId;

        /// <summary>
        /// The Base64-encoded representation of "clientId:clientSecret", ready for use in HTTP Basic
        /// authentication headers. This is the format required by the SoundCloud token endpoint per
        /// OAuth 2.0 client credentials specification (RFC 6749, Section 2.3.1).
        /// </summary>
        public string Credentials { get; }
            = Convert.ToBase64String( Encoding.UTF8.GetBytes( $"{clientId}:{clientSecret}" ) );
    }

}
