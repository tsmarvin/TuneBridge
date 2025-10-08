using System.ComponentModel;

namespace TuneBridge.Domain.Types.Enums {
    /// <summary>
    /// Identifies the supported music streaming platforms for cross-platform lookup and link translation.
    /// Each provider requires different API credentials, authentication methods, and has unique catalog
    /// availability. Used throughout the application to route requests to the appropriate service implementations.
    /// </summary>
    /// <remarks>
    /// The Description attribute provides human-readable names for UI display. New providers can be added
    /// by extending this enum and implementing the <see cref="IMusicLookupService"/> interface.
    /// </remarks>
    public enum SupportedProviders {
        /// <summary>
        /// Apple Music streaming service (music.apple.com). Requires MusicKit API credentials (Team ID,
        /// Key ID, and private key .p8 file). Uses JWT authentication. Strong in Western markets and
        /// particularly dominant in US iOS user base.
        /// </summary>
        /// <remarks>
        /// API Documentation: https://developer.apple.com/documentation/applemusicapi
        /// Credentials from: https://developer.apple.com/account/
        /// </remarks>
        [Description("Apple Music")]
        AppleMusic = 1,

        /// <summary>
        /// Spotify streaming service (open.spotify.com). Requires Spotify for Developers app credentials
        /// (Client ID and Client Secret). Uses OAuth 2.0 client credentials flow. Largest global market
        /// share with over 500M users. Generally has better API documentation and rate limits.
        /// </summary>
        /// <remarks>
        /// API Documentation: https://developer.spotify.com/documentation/web-api
        /// Credentials from: https://developer.spotify.com/dashboard
        /// </remarks>
        Spotify    = 2,
    }
}
