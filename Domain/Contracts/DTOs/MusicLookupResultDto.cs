namespace TuneBridge.Domain.Contracts.DTOs {

    /// <summary>
    /// Represents music metadata returned from a provider-specific API query. This DTO encapsulates
    /// all essential information needed to identify and link to a track or album on a streaming platform,
    /// including unique identifiers (ISRC/UPC) for cross-platform matching.
    /// </summary>
    /// <remarks>
    /// Two instances are considered equal if they have the same ExternalId, Artist, Title, URL, ArtUrl,
    /// MarketRegion, and IsAlbum values. The IsPrimary flag is excluded from equality checks as it's
    /// an internal processing hint rather than identifying information.
    /// </remarks>
    public sealed class MusicLookupResultDto {

        /// <summary>
        /// The primary artist name as returned by the music provider's API. For tracks, this is typically
        /// the first/main artist even if multiple artists are credited. For albums, this is the album artist.
        /// </summary>
        /// <example>
        /// "Taylor Swift", "The Beatles", "Daft Punk"
        /// </example>
        public string Artist { get; set; } = string.Empty;

        /// <summary>
        /// The official title of the track or album as listed in the provider's catalog. May include
        /// additional descriptors like "(Deluxe Edition)", "(Remastered)", or "- Single" depending on
        /// how the provider formats their metadata.
        /// </summary>
        /// <example>
        /// "Shake It Off", "Abbey Road (Remastered)", "Random Access Memories (Deluxe Edition)"
        /// </example>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The standardized global identifier for the recording or album. For tracks, this is the ISRC
        /// (International Standard Recording Code). For albums, this is the UPC (Universal Product Code).
        /// These IDs enable reliable cross-platform matching since they're consistent across all providers.
        /// </summary>
        /// <example>
        /// ISRC: "USRC17607839" (for tracks)
        /// UPC: "00602537518357" (for albums)
        /// </example>
        /// <remarks>
        /// Empty string if the provider didn't return an external ID. Some older or regional catalog
        /// items may lack standardized identifiers.
        /// </remarks>
        public string ExternalId { get; set; } = string.Empty;

        /// <summary>
        /// Direct web link to the track or album on the provider's platform (e.g., Apple Music web player,
        /// Spotify web player). These URLs are shareable and will open in the respective service's app
        /// if installed on the user's device.
        /// </summary>
        /// <example>
        /// "https://music.apple.com/us/album/1440857781?i=1440857907"
        /// "https://open.spotify.com/track/0cqRj7pUJDkTCEsJkx8snD"
        /// </example>
        public string URL { get; set; } = string.Empty;

        /// <summary>
        /// URL to the cover artwork image. For tracks, this is the album artwork. For albums, this is
        /// the main album cover. Image sizes vary by provider but are typically at least 640x640 pixels.
        /// Empty string if no artwork is available.
        /// </summary>
        /// <example>
        /// "https://is1-ssl.mzstatic.com/image/thumb/Music/v4/..."
        /// "https://i.scdn.co/image/ab67616d0000b273..."
        /// </example>
        public string ArtUrl { get; set; } = string.Empty;

        /// <summary>
        /// The market/storefront code indicating which regional catalog this result came from. Different
        /// markets may have different availability, pricing, and even different versions of the same content.
        /// Uses ISO 3166-1 alpha-2 country codes.
        /// </summary>
        /// <example>
        /// "us" (United States), "gb" (United Kingdom), "jp" (Japan)
        /// </example>
        /// <remarks>
        /// Defaults to "us" if not specified. This affects which catalog is searched and which URLs are returned.
        /// </remarks>
        public string MarketRegion { get; set; } = "us";

        /// <summary>
        /// Discriminates between album and track results. True indicates an album/EP, false indicates
        /// a single track/song. Null if the content type couldn't be determined (rare edge case).
        /// </summary>
        /// <remarks>
        /// This flag affects which external ID type is expected (UPC for albums, ISRC for tracks) and
        /// influences how the result is displayed in the UI (album icon vs track icon, etc.).
        /// </remarks>
        public bool? IsAlbum { get; set; }

        /// <summary>
        /// Internal flag indicating this result came from the provider that was directly queried (as opposed
        /// to being found via cross-platform lookup). Used during result aggregation to prioritize data from
        /// the original source. Not serialized and excluded from equality comparisons.
        /// </summary>
        /// <remarks>
        /// When a user shares a Spotify link, the Spotify result is marked as primary. The corresponding
        /// Apple Music result (if found) is secondary. This helps preserve the original link's metadata
        /// preferences when there are conflicts between providers.
        /// </remarks>
        internal bool IsPrimary { get; set; }

        public override bool Equals( object? obj ) {
            if (
                obj is not null &&
                obj.GetType( ) == typeof( MusicLookupResultDto )
            ) {
                MusicLookupResultDto objCast = (MusicLookupResultDto)obj;
                return
                    objCast.ExternalId == ExternalId &&
                    objCast.Artist == Artist &&
                    objCast.Title == Title &&
                    objCast.URL == URL &&
                    objCast.ArtUrl == ArtUrl &&
                    objCast.MarketRegion == MarketRegion &&
                    objCast.IsAlbum == IsAlbum;
            }
            return false;
        }

        public override int GetHashCode( ) {
            return Artist.GetHashCode( ) +
            Title.GetHashCode( ) +
            ExternalId.GetHashCode( ) +
            URL.GetHashCode( ) +
            ArtUrl.GetHashCode( ) +
            MarketRegion.GetHashCode( ) +
            IsAlbum.GetHashCode( );
        }
    }

}
