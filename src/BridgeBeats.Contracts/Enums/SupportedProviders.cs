using System.ComponentModel;
using BridgeBeats.Contracts.Interfaces;

namespace BridgeBeats.Contracts.Enums;

/// <summary>
/// Identifies a music streaming provider that BridgeBeats can resolve links for. Used as the key for
/// provider-scoped dictionaries, queues, and saga state across the solution.
/// </summary>
/// <remarks>
/// New providers are added by extending this enum and implementing <see cref="IMusicLookupService"/>.
/// Values are explicit and non-zero: there is no <c>Unknown</c> / 0 member because every value names
/// a real provider. <see cref="DescriptionAttribute"/> supplies a display name where it differs from
/// the member name.
/// </remarks>
public enum SupportedProviders {

    /// <summary>
    /// Apple Music (displays as "Apple Music"). Authenticated with a MusicKit JWT built from the
    /// Team ID, Key ID, and private key (.p8) credentials.
    /// </summary>
    /// <remarks>
    /// API documentation: https://developer.apple.com/documentation/applemusicapi.
    /// Credentials: https://developer.apple.com/account/.
    /// </remarks>
    [Description("Apple Music")]
    AppleMusic = 1,

    /// <summary>
    /// Spotify (the member name doubles as the display name). Authenticated with the OAuth 2.0 client
    /// credentials flow using a Client ID and Client Secret.
    /// </summary>
    /// <remarks>
    /// API documentation: https://developer.spotify.com/documentation/web-api.
    /// Credentials: https://developer.spotify.com/dashboard.
    /// </remarks>
    Spotify = 2,

    /// <summary>
    /// Tidal (displays as "Tidal"). Authenticated with the OAuth 2.0 client credentials flow using a
    /// Client ID and Client Secret.
    /// </summary>
    /// <remarks>
    /// API documentation: https://developer.tidal.com/documentation/api/api-overview.
    /// Credentials: https://developer.tidal.com/.
    /// </remarks>
    [Description("Tidal")]
    Tidal = 3,
}
