using System.ComponentModel;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Types.Enums {
    /// <summary>
    /// Identifies the supported music streaming platforms for cross-platform lookup and link translation.
    /// </summary>
    /// <remarks>
    /// New providers can be added by extending this enum and implementing the <see cref="IMusicLookupService"/> interface.
    /// </remarks>
    public enum SupportedProviders {
        /// <summary>
        /// Apple Music streaming service. Requires MusicKit API credentials (Team ID, Key ID, and private key .p8 file).
        /// Uses JWT authentication.
        /// </summary>
        /// <remarks>
        /// API Documentation: https://developer.apple.com/documentation/applemusicapi
        /// Credentials from: https://developer.apple.com/account/
        /// </remarks>
        [Description("Apple Music")]
        AppleMusic = 1,

        /// <summary>
        /// Spotify streaming service. Requires Spotify for Developers app credentials (Client ID and Client Secret).
        /// Uses OAuth 2.0 client credentials flow.
        /// </summary>
        /// <remarks>
        /// API Documentation: https://developer.spotify.com/documentation/web-api
        /// Credentials from: https://developer.spotify.com/dashboard
        /// </remarks>
        Spotify    = 2,
    }
}
