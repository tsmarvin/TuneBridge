using System.Text.RegularExpressions;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.LinkParsers {

    /// <summary>
    /// Utility class for parsing Spotify URLs and constructing Spotify Web API request URIs.
    /// Extracts entity types (track, album, artist, playlist) and IDs from open.spotify.com URLs,
    /// then maps them to the corresponding API v1 endpoints for metadata retrieval.
    /// </summary>
    /// <remarks>
    /// Spotify uses a consistent URL structure: open.spotify.com/{type}/{id} where {type} is
    /// "track", "album", "artist", or "playlist", and {id} is a Base62-encoded identifier.
    /// This parser validates the URL structure and extracts both components for API calls.
    /// </remarks>
    internal static partial class SpotifyLinkParser {

        /// <summary>
        /// Parses a Spotify web URL to extract the entity type and Spotify ID. Validates URL structure
        /// and determines whether the link points to a track, album, artist, or playlist.
        /// </summary>
        /// <param name="link">
        /// Spotify URL in the format "https://open.spotify.com/{type}/{id}" or "https://spotify.link/{code}".
        /// Must follow the open.spotify.com or spotify.link pattern. Query parameters are ignored.
        /// </param>
        /// <param name="kind">
        /// Output: The entity type extracted from the URL, mapped to <see cref="SpotifyEntity"/> enum.
        /// Set to <see cref="SpotifyEntity.Unknown"/> if the URL doesn't match known patterns.
        /// </param>
        /// <param name="id">
        /// Output: The Spotify ID (Base62 alphanumeric string, typically 22 characters).
        /// This ID can be used directly in Spotify Web API v1 endpoints. Empty string if parsing fails.
        /// </param>
        /// <returns>
        /// True if the URL was successfully parsed and recognized as a supported Spotify entity type.
        /// False if the URL is malformed, doesn't match Spotify patterns, or refers to an unsupported entity.
        /// </returns>
        /// <remarks>
        /// Only track and album URLs are currently utilized for music lookup. Artist and playlist URLs
        /// are parsed but may not be fully supported by all downstream operations.
        /// For spotify.link URLs, the method follows the redirect to obtain the actual open.spotify.com URL.
        /// </remarks>
        public static bool TryParseUri(
            string link,
            out SpotifyEntity kind,
            out string id
        ) {
            kind = SpotifyEntity.Unknown;
            id = string.Empty;

            // If it's a spotify.link URL, resolve it to the actual Spotify URL
            if (s_spotifyShortLink.IsMatch( link )) {
                string? resolvedUrl = ResolveSpotifyShortLink( link );
                if (resolvedUrl != null) {
                    link = resolvedUrl;
                }
            }

            if (s_spotifyLink.IsMatch( link )) {

                string? uri = s_spotifyLink.GetGroupValues(link, "type").FirstOrDefault();
                if (uri != null) {
                    Match match = s_spotifyLink.Match(link);

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

        /// <summary>
        /// Resolves a spotify.link short URL to the actual open.spotify.com URL by following the HTTP redirect.
        /// </summary>
        /// <param name="shortLink">The spotify.link URL to resolve.</param>
        /// <returns>The resolved open.spotify.com URL, or null if resolution fails.</returns>
        private static string? ResolveSpotifyShortLink( string shortLink ) {
            using HttpClient client = new( new HttpClientHandler { AllowAutoRedirect = false } );
            HttpResponseMessage response = client.GetAsync( $"https://{shortLink}" ).Result;
            return response.StatusCode is System.Net.HttpStatusCode.MovedPermanently or
                   System.Net.HttpStatusCode.Found or
                   System.Net.HttpStatusCode.SeeOther or
                   System.Net.HttpStatusCode.TemporaryRedirect
                ? (response.Headers.Location?.ToString( ))
                : null;
        }

        private static readonly Regex s_spotifyLink = SpotifyMusicLink();
        [GeneratedRegex( @"(?:open\.spotify\.com/)(?<type>track|album)/(?<id>[A-Za-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex SpotifyMusicLink( );

        private static readonly Regex s_spotifyShortLink = SpotifyShortLinkPattern();
        [GeneratedRegex( @"spotify\.link/[A-Za-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex SpotifyShortLinkPattern( );

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
