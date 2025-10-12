using System.Text.RegularExpressions;
using TuneBridge.Domain.Implementations.Extensions;
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

            if (TidalLink.IsMatch( link )) {

                string? uri = TidalLink.GetGroupValues(link, "type").FirstOrDefault();
                if (uri != null) {
                    Match match = TidalLink.Match(link);

                    id = match.Groups["id"].Value;
                    kind = match.Groups["type"].Value.ToLowerInvariant( ) switch {
                        "track" => TidalEntity.Track,
                        "album" => TidalEntity.Album,
                        "artist" => TidalEntity.Artist,
                        _ => TidalEntity.Unknown
                    };
                }
            }

            return kind != TidalEntity.Unknown && !string.IsNullOrEmpty( id );
        }

        private static readonly Regex TidalLink = TidalMusicLink();
        [GeneratedRegex( @"(?:(?:listen\.)?tidal\.com/)(?:browse/)?(?<type>track|album|artist)/(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex TidalMusicLink( );

        /// <summary>
        /// Constructs an API URI for searching artists by name.
        /// </summary>
        /// <param name="artist">The artist name to search for.</param>
        /// <returns>The API URI for artist search.</returns>
        public static string GetArtistSearchUri( string artist )
            => ArtistsSearchURI.Replace( "{artist}", Uri.EscapeDataString( artist ) );

        /// <summary>
        /// Constructs an API URI for looking up a track by ISRC.
        /// </summary>
        /// <param name="isrc">The ISRC code.</param>
        /// <returns>The API URI for ISRC lookup.</returns>
        public static string GetTracksIsrcURI( string isrc )
            => TracksIsrcURI.Replace( "{isrc}", isrc );

        /// <summary>
        /// Constructs an API URI for looking up an album by UPC.
        /// </summary>
        /// <param name="upc">The UPC code.</param>
        /// <returns>The API URI for UPC lookup.</returns>
        public static string GetAlbumUpcURI( string upc )
            => AlbumsUpcURI.Replace( "{upc}", upc );

        /// <summary>
        /// Constructs an API URI for getting an artist's albums.
        /// </summary>
        /// <param name="artistId">The artist's Tidal ID.</param>
        /// <returns>The API URI for the artist's albums.</returns>
        public static string GetArtistAlbumsURI( string artistId )
            => ArtistAlbumsURI
                .Replace( "{id}", artistId );

        /// <summary>
        /// Constructs an API URI for getting an album's tracks.
        /// </summary>
        /// <param name="albumId">The album's Tidal ID.</param>
        /// <returns>The API URI for the album's tracks.</returns>
        public static string GetAlbumTracksURI( string albumId )
            => AlbumTracksURI
                .Replace( "{id}", albumId );

        /// <summary>
        /// Constructs an API URI for getting album details by ID.
        /// </summary>
        /// <param name="albumId">The album's Tidal ID.</param>
        /// <returns>The API URI for the album.</returns>
        public static string GetAlbumIdURI( string albumId )
            => AlbumsURI
                .Replace( "{id}", albumId );

        /// <summary>
        /// Constructs an API URI for getting track details by ID.
        /// </summary>
        /// <param name="trackId">The track's Tidal ID.</param>
        /// <returns>The API URI for the track.</returns>
        public static string GetTrackIdURI( string trackId )
            => TracksURI
                .Replace( "{id}", trackId );


        private const string TracksIsrcURI = "search/tracks?query={isrc}&limit=1&countryCode=US";
        private const string AlbumsUpcURI = "search/albums?query={upc}&limit=1&countryCode=US";
        private const string ArtistsSearchURI = "search/artists?query={artist}&limit=10&countryCode=US";
        private const string ArtistAlbumsURI = "artists/{id}/albums?countryCode=US&limit=50";
        private const string AlbumTracksURI = "albums/{id}/tracks?countryCode=US&limit=100";
        private const string AlbumsURI = "albums/{id}?countryCode=US";
        private const string TracksURI = "tracks/{id}?countryCode=US";

    }
}
