namespace TuneBridge.Domain.Implementations.Auth {

    /// <summary>
    /// Encapsulates SoundCloud API credentials. SoundCloud API v2 currently uses client_id 
    /// as a query parameter for authentication rather than OAuth tokens.
    /// This class is immutable and thread-safe, designed to be registered as a singleton in dependency injection.
    /// </summary>
    /// <param name="clientId">
    /// The Client ID from your SoundCloud app. This is used as a query parameter in API requests.
    /// </param>
    /// <remarks>
    /// Credentials are obtained by requesting API access via SoundCloud support.
    /// See: https://help.soundcloud.com/hc/en-us/requests/new
    /// </remarks>
    public sealed class SoundCloudCredentials(
        string clientId
    ) {
        /// <summary>
        /// The Client ID for SoundCloud API requests. Used as a query parameter in API calls.
        /// </summary>
        public string ClientId { get; } = clientId;
    }

}
