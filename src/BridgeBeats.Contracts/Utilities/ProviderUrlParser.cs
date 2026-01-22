using System.Text.RegularExpressions;
using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Utilities;

/// <summary>
/// Shared utility for parsing provider URLs and extracting IDs.
/// Contains regex patterns and extraction logic used by both Infrastructure and Providers layers.
/// </summary>
public static partial class ProviderUrlParser {

    // Regex patterns for URL ID extraction
    private static readonly Regex s_appleMusicSongIdRegex = AppleMusicSongIdRegex( );
    private static readonly Regex s_appleMusicLinkRegex = AppleMusicLinkRegex( );
    private static readonly Regex s_spotifyLinkRegex = SpotifyLinkRegex( );
    private static readonly Regex s_tidalLinkRegex = TidalLinkRegex( );

    [GeneratedRegex( @"\?i\=(?<songId>[^&#]*)" )]
    private static partial Regex AppleMusicSongIdRegex( );

    [GeneratedRegex( @"[Mm][Uu][Ss][Ii][Cc]\.[Aa][Pp][Pp][Ll][Ee]\.[Cc][Oo][Mm]/(?<URI>[_\w\d\/\=\?\.\:\-%&]*)" )]
    private static partial Regex AppleMusicLinkRegex( );

    [GeneratedRegex( @"(?:open\.spotify\.com/)(?<type>track|album|prerelease)/(?<id>[A-Za-z0-9]+)", RegexOptions.IgnoreCase )]
    private static partial Regex SpotifyLinkRegex( );

    [GeneratedRegex( @"(?:(?:listen\.)?tidal\.com/)(?:browse/)?(?<type>track|album)/(?<id>\d+)", RegexOptions.IgnoreCase )]
    private static partial Regex TidalLinkRegex( );

    /// <summary>
    /// Extracts the provider-specific ID from a URL.
    /// </summary>
    /// <param name="provider">The provider type.</param>
    /// <param name="url">The URL to parse.</param>
    /// <returns>The extracted ID, or null if the URL is invalid or cannot be parsed.</returns>
    public static string? ExtractId( SupportedProviders provider, string url ) {
        if (string.IsNullOrWhiteSpace( url )) {
            return null;
        }

        return provider switch {
            SupportedProviders.AppleMusic => ExtractAppleMusicId( url ),
            SupportedProviders.Spotify => ExtractSpotifyId( url ),
            SupportedProviders.Tidal => ExtractTidalId( url ),
            _ => null
        };
    }

    /// <summary>
    /// Extracts the Apple Music catalog ID from a URL.
    /// </summary>
    /// <param name="url">The Apple Music URL to parse.</param>
    /// <returns>The extracted ID, or null if the URL is invalid or cannot be parsed.</returns>
    /// <remarks>
    /// Handles both album and song URLs. For album URLs with ?i= query parameter,
    /// returns the song ID from the query parameter. Otherwise returns the primary ID
    /// from the URL path.
    /// </remarks>
    public static string? ExtractAppleMusicId( string url ) {
        if (string.IsNullOrWhiteSpace( url )) {
            return null;
        }

        try {
            // Check for ?i= query parameter (song ID in album URL)
            Match songIdMatch = s_appleMusicSongIdRegex.Match( url );
            if (songIdMatch.Success) {
                string songId = songIdMatch.Groups["songId"].Value;
                if (!string.IsNullOrWhiteSpace( songId )) {
                    return songId;
                }
            }

            // Try to parse as regular Apple Music URL
            Match match = s_appleMusicLinkRegex.Match( url );
            if (match.Success) {
                string? uri = match.Groups["URI"].Value;
                if (!string.IsNullOrWhiteSpace( uri )) {
                    // Extract ID from URI (last segment before query params)
                    string id = uri.Split( '/' ).Last( ).Split( '?' )[0];
                    if (!string.IsNullOrWhiteSpace( id )) {
                        return id;
                    }
                }
            }
        } catch {
            // Return null on any parsing error
        }

        return null;
    }

    /// <summary>
    /// Extracts the Spotify track or album ID from a URL.
    /// </summary>
    /// <param name="url">The Spotify URL to parse.</param>
    /// <returns>The extracted ID, or null if the URL is invalid or cannot be parsed.</returns>
    /// <remarks>
    /// This is a synchronous method that only handles direct open.spotify.com URLs.
    /// It does not resolve spotify.link short URLs.
    /// </remarks>
    public static string? ExtractSpotifyId( string url ) {
        if (string.IsNullOrWhiteSpace( url )) {
            return null;
        }

        try {
            Match match = s_spotifyLinkRegex.Match( url );
            if (match.Success) {
                string id = match.Groups["id"].Value;
                if (!string.IsNullOrWhiteSpace( id )) {
                    return id;
                }
            }
        } catch {
            // Return null on any parsing error
        }

        return null;
    }

    /// <summary>
    /// Extracts the Tidal track or album ID from a URL.
    /// </summary>
    /// <param name="url">The Tidal URL to parse.</param>
    /// <returns>The extracted ID, or null if the URL is invalid or cannot be parsed.</returns>
    /// <remarks>
    /// Handles both tidal.com and listen.tidal.com URLs. Returns the numeric ID
    /// for tracks and albums only. Artist IDs are not returned.
    /// </remarks>
    public static string? ExtractTidalId( string url ) {
        if (string.IsNullOrWhiteSpace( url )) {
            return null;
        }

        try {
            Match match = s_tidalLinkRegex.Match( url );
            if (match.Success) {
                string type = match.Groups["type"].Value;
                
                // Only extract IDs for tracks and albums
                if (type.Equals( "track", StringComparison.OrdinalIgnoreCase ) ||
                    type.Equals( "album", StringComparison.OrdinalIgnoreCase )) {
                    string id = match.Groups["id"].Value;
                    if (!string.IsNullOrWhiteSpace( id )) {
                        return id;
                    }
                }
            }
        } catch {
            // Return null on any parsing error
        }

        return null;
    }
}
