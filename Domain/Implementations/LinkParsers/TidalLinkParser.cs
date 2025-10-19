using System.Text.RegularExpressions;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.LinkParsers {

    /// <summary>
    /// Utility class for parsing Tidal URLs and constructing Tidal API request URIs.
    /// Extracts entity types (track, album, artist) and IDs from tidal.com URLs,
    /// then maps them to the corresponding API v1 endpoints for metadata retrieval.
    /// </summary>
    /// <remarks>
    /// Tidal uses a consistent URL structure: tidal.com/browse/{type}/{id} where {type} is
    /// "track", "album", or "artist", and {id} is a numeric identifier.
    /// This parser validates the URL structure and extracts both components for API calls.
    /// </remarks>
    internal static partial class TidalLinkParser {

        /// <summary>
        /// Parses a Tidal web URL to extract the entity type and Tidal ID. Validates URL structure
        /// and determines whether the link points to a track, album, or artist.
        /// </summary>
        /// <param name="link">
        /// Tidal URL in the format "https://tidal.com/browse/{type}/{id}" or "https://listen.tidal.com/{type}/{id}".
        /// Must follow the tidal.com or listen.tidal.com pattern. Query parameters are ignored.
        /// </param>
        /// <param name="kind">
        /// Output: The entity type extracted from the URL, mapped to <see cref="TidalEntity"/> enum.
        /// Set to <see cref="TidalEntity.Unknown"/> if the URL doesn't match known patterns.
        /// </param>
        /// <param name="id">
        /// Output: The Tidal ID (numeric string).
        /// This ID can be used directly in Tidal API v1 endpoints. Empty string if parsing fails.
        /// </param>
        /// <returns>
        /// True if the URL was successfully parsed and recognized as a supported Tidal entity type.
        /// False if the URL is malformed, doesn't match Tidal patterns, or refers to an unsupported entity.
        /// </returns>
        /// <remarks>
        /// Only track and album URLs are currently utilized for music lookup. Artist URLs
        /// are parsed but may not be fully supported by all downstream operations.
        /// </remarks>
        public static bool TryParseUri(
            string link,
            out TidalEntity kind,
            out string id
        ) {
            kind = TidalEntity.Unknown;
            id = string.Empty;

            if (s_tidalLink.IsMatch( link )) {
                Match tidalMatch = s_tidalLink.Match(link);

                if (tidalMatch.Groups.ContainsKey( "type" )) {
                    kind = tidalMatch.Groups["type"].Value switch {
                        "track" => TidalEntity.Track,
                        "album" => TidalEntity.Album,
                        "artist" => TidalEntity.Artist,
                        _ => TidalEntity.Unknown,
                    };
                }

                ;

                if (tidalMatch.Groups.ContainsKey( "id" )) {
                    id = tidalMatch.Groups["id"].Value;
                }
            }

            return (kind == TidalEntity.Track || kind == TidalEntity.Album) && !string.IsNullOrEmpty( id );
        }

        private static readonly Regex s_tidalLink = TidalMusicLink();
        [GeneratedRegex( @"(?:(?:listen\.)?tidal\.com/)(?:browse/)?(?<type>track|album)/(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex TidalMusicLink( );

        /// <summary>
        /// Constructs an API URI for searching artists by name.
        /// </summary>
        /// <param name="artist">The artist name to search for.</param>
        /// <returns>The API URI for artist search.</returns>
        public static string GetArtistSearchUri( string storefront, string artist )
            => ArtistSearchURI
                .Replace( "{storefront}", storefront )
                .Replace( "{artist}", Uri.EscapeDataString( artist ) );

        /// <summary>
        /// Constructs an API URI for searching artists track titles by name.
        /// </summary>
        /// <param name="artistId">The artist to get tracks for.</param>
        /// <returns>The API URI for artist tracks to search.</returns>
        public static string GetArtistTracksUri( string storefront, string artistId )
            => ArtistTrackRelationshipsUri
                .Replace( "{storefront}", storefront )
                .Replace( "{artistId}", artistId );

        /// <summary>
        /// Constructs an API URI for searching artists album titles by name.
        /// </summary>
        /// <param name="artistId">The artist to get albums for.</param>
        /// <returns>The API URI for artist albums to search.</returns>
        public static string GetArtistAlbumsUri( string storefront, string artistId )
            => ArtistAlbumRelationshipsUri
                .Replace( "{storefront}", storefront )
                .Replace( "{artistId}", artistId );

        /// <summary>
        /// Constructs an API URI for looking up a track by ISRC.
        /// </summary>
        /// <param name="isrc">The ISRC code.</param>
        /// <returns>The API URI for ISRC lookup.</returns>
        public static string GetTracksIsrcURI( string storefront, string isrc )
            => TracksIsrcURI
                .Replace( "{storefront}", storefront )
                .Replace( "{isrc}", isrc );

        /// <summary>
        /// Constructs an API URI for looking up an album by UPC.
        /// </summary>
        /// <param name="upc">The UPC code.</param>
        /// <returns>The API URI for UPC lookup.</returns>
        public static string GetAlbumUpcURI( string storefront, string upc )
            => AlbumsUpcURI
                .Replace( "{storefront}", storefront )
                .Replace( "{upc}", upc );

        /// <summary>
        /// Constructs an API URI for looking up an album by its Tidal Id.
        /// </summary>
        /// <param name="albumId">The tidal album id to search for.</param>
        /// <returns>The API URI for album lookup by id.</returns>
        public static string GetAlbumIdURI( string storefront, string albumId )
            => AlbumIdUri
                .Replace( "{storefront}", storefront )
                .Replace( "{albumId}", albumId );

        /// <summary>
        /// Constructs an API URI for looking up a track by its Tidal Id.
        /// </summary>
        /// <param name="trackId">The tidal track id to search for.</param>
        /// <returns>The API URI for track lookup by id.</returns>
        public static string GetTrackIdURI( string storefront, string trackId )
            => TrackIdUri
                .Replace( "{storefront}", storefront )
                .Replace( "{trackId}", trackId );

        private const string TracksIsrcURI = "tracks?filter%5Bisrc%5D={isrc}&countryCode={storefront}&include=albums&include=artists";
        private const string AlbumsUpcURI = "albums?filter%5BbarcodeId%5D={upc}&countryCode={storefront}&include=artists&include=coverArt";
        private const string ArtistSearchURI = "searchResults/{artist}?countryCode={storefront}&explicitFilter=include&include=artists";
        private const string ArtistAlbumRelationshipsUri = "artists/{artistId}/relationships/albums?countryCode={storefront}&include=albums";
        private const string ArtistTrackRelationshipsUri = "artists/{artistId}/relationships/tracks?countryCode={storefront}&collapseBy=FINGERPRINT&include=tracks";
        private const string TrackIdUri = "tracks/{trackId}?countryCode={storefront}&include=albums&include=artists";
        private const string AlbumIdUri = "albums/{albumId}?countryCode={storefront}&include=artists&include=coverArt";

    }
}
