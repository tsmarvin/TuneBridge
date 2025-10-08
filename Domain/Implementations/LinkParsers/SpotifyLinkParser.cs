using System.Text.RegularExpressions;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.LinkParsers {

    /// <summary>
    /// Parses Spotify URLs and constructs API request URIs for the Spotify API.
    /// </summary>
    internal static partial class SpotifyLinkParser {

        /// <summary>
        /// Attempts to parse a Spotify URL to extract the entity type and ID.
        /// </summary>
        /// <param name="link">The Spotify URL to parse.</param>
        /// <param name="kind">The type of entity (Track, Album, etc.).</param>
        /// <param name="id">The Spotify ID of the entity.</param>
        /// <returns>True if the URL was successfully parsed, false otherwise.</returns>
        public static bool TryParseUri(
            string link,
            out SpotifyEntity kind,
            out string id
        ) {
            kind = SpotifyEntity.Unknown;
            id = string.Empty;

            if (SpotifyLink.IsMatch( link )) {

                string? uri = SpotifyLink.GetGroupValues(link, "type").FirstOrDefault();
                if (uri != null) {
                    Match match = SpotifyLink.Match(link);

                    id = match.Groups["id"].Value;
                    kind = match.Groups["type"].Value.ToLowerInvariant( ) switch {
                        "track" => SpotifyEntity.Track,
                        "album" => SpotifyEntity.Album,
                        _ => SpotifyEntity.Unknown
                    };
                }
            }

            return kind != SpotifyEntity.Unknown && !string.IsNullOrEmpty( id );
        }

        private static readonly Regex SpotifyLink = SpotifyMusicLink();
        [GeneratedRegex( @"(?:open\.spotify\.com/)(?<type>track|album)/(?<id>[A-Za-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex SpotifyMusicLink( );

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
        /// <param name="artistId">The artist's Spotify ID.</param>
        /// <returns>The API URI for the artist's albums.</returns>
        public static string GetArtistAlbumsURI( string artistId )
            => ArtistAlbumsURI
                .Replace( "{id}", artistId );

        /// <summary>
        /// Constructs an API URI for getting an album's tracks.
        /// </summary>
        /// <param name="albumId">The album's Spotify ID.</param>
        /// <returns>The API URI for the album's tracks.</returns>
        public static string GetAlbumTracksURI( string albumId )
            => AlbumTracksURI
                .Replace( "{id}", albumId );

        /// <summary>
        /// Constructs an API URI for getting album details by ID.
        /// </summary>
        /// <param name="albumId">The album's Spotify ID.</param>
        /// <returns>The API URI for the album.</returns>
        public static string GetAlbumIdURI( string albumId )
            => AlbumsURI
                .Replace( "{id}", albumId );

        /// <summary>
        /// Constructs an API URI for getting track details by ID.
        /// </summary>
        /// <param name="trackId">The track's Spotify ID.</param>
        /// <returns>The API URI for the track.</returns>
        public static string GetTrackIdURI( string trackId )
            => TracksURI
                .Replace( "{id}", trackId );


        private const string TracksIsrcURI = "search?q=isrc:{isrc}&type=track";
        private const string AlbumsUpcURI = "search?q=upc:{upc}&type=album";
        private const string ArtistsSearchURI = "search?q={artist}&type=artist";
        private const string ArtistAlbumsURI = "artists/{id}/albums";
        private const string AlbumTracksURI = "albums/{id}/tracks";
        private const string AlbumsURI = "albums/{id}";
        private const string TracksURI = "tracks/{id}";

    }
}
