using System.Text.RegularExpressions;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Utilities;

namespace BridgeBeats.Providers.Spotify {

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
    public static partial class SpotifyLinkParser {

        /// <summary>
        /// Parses a Spotify web URL to extract the entity type and Spotify ID. Validates URL structure
        /// and determines whether the link points to a track, album, artist, or playlist.
        /// </summary>
        /// <param name="link">
        /// Spotify URL in the format "https://open.spotify.com/{type}/{id}" or "https://spotify.link/{code}".
        /// Must follow the open.spotify.com or spotify.link pattern. Query parameters are ignored.
        /// </param>
        /// <returns>
        /// A tuple containing: (bool success, SpotifyEntity kind, string id).
        /// - success: True if the URL was successfully parsed and recognized.
        /// - kind: The entity type extracted from the URL.
        /// - id: The Spotify ID (Base62 alphanumeric string, typically 22 characters).
        /// </returns>
        /// <remarks>
        /// Only track and album URLs are currently utilized for music lookup. Artist and playlist URLs
        /// are parsed but may not be fully supported by all downstream operations.
        /// For spotify.link URLs, the method follows the redirect to obtain the actual open.spotify.com URL.
        /// </remarks>
        public static async Task<(bool, SpotifyEntity kind, string id)> TryParseUriAsync(
            string link
        ) {
            SpotifyEntity kind = SpotifyEntity.Unknown;
            string id = string.Empty;

            // If it's a spotify.link URL, resolve it to the actual Spotify URL
            if (s_spotifyShortLink.IsMatch( link )) {
                string? resolvedUrl = await ResolveSpotifyShortLink( link );
                if (resolvedUrl != null) {
                    link = resolvedUrl;
                }
            }

            if (s_spotifyLink.IsMatch( link )) {
                Match match = s_spotifyLink.Match(link);

                id = match.Groups["id"].Value;
                kind = match.Groups["type"].Value.ToLowerInvariant( ) switch {
                    "track" => SpotifyEntity.Track,
                    "album" => SpotifyEntity.Album,
                    "prerelease" => SpotifyEntity.PreRelease,
                    "playlists" => SpotifyEntity.Playlist,
                    _ => SpotifyEntity.Unknown
                };
            }

            return (kind != SpotifyEntity.Unknown && !string.IsNullOrEmpty( id ), kind, id);
        }

        /// <summary>
        /// Resolves a spotify.link short URL to the actual open.spotify.com URL by following the HTTP redirect.
        /// </summary>
        /// <param name="shortLink">The spotify.link URL to resolve.</param>
        /// <returns>The resolved open.spotify.com URL, or null if resolution fails.</returns>
        private static async Task<string?> ResolveSpotifyShortLink( string shortLink ) {
            using HttpClient client = new( new HttpClientHandler { AllowAutoRedirect = false } );
            client.Timeout = TimeSpan.FromSeconds( 5 );
            HttpResponseMessage response = await client.GetAsync( $"https://{shortLink}" );
            return response.StatusCode is System.Net.HttpStatusCode.MovedPermanently or
                   System.Net.HttpStatusCode.Found or
                   System.Net.HttpStatusCode.SeeOther or
                   System.Net.HttpStatusCode.TemporaryRedirect
                ? (response.Headers.Location?.ToString( ))
                : null;
        }

        private static readonly Regex s_spotifyLink = SpotifyMusicLink();
        [GeneratedRegex( @"(?:open\.spotify\.com/)(?<type>track|album|prerelease)/(?<id>[A-Za-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex SpotifyMusicLink( );

        private static readonly Regex s_spotifyShortLink = SpotifyShortLinkPattern();
        [GeneratedRegex( @"spotify\.link/[A-Za-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex SpotifyShortLinkPattern( );

        /// <summary>
        /// Extracts the Spotify ID (track or album) from a URL.
        /// </summary>
        /// <param name="url">The Spotify URL to parse.</param>
        /// <returns>The extracted ID, or null if the URL is invalid or cannot be parsed.</returns>
        /// <remarks>
        /// This is a synchronous method that only handles direct open.spotify.com URLs.
        /// It does not resolve spotify.link short URLs. For full URL resolution including
        /// short links, use <see cref="TryParseUriAsync"/>.
        /// </remarks>
        public static string? ExtractId( string url )
            => ProviderUrlParser.ExtractSpotifyId( url );

        /// <summary>
        /// Normalizes a Spotify URL by removing query parameters and fragments.
        /// Reconstructs a clean canonical URL from the ID and entity type.
        /// </summary>
        /// <param name="url">The Spotify URL to normalize (may contain query parameters like ?si=).</param>
        /// <returns>
        /// The normalized URL without query parameters, or the original URL if it cannot be parsed.
        /// Returns empty string if the input is null or empty.
        /// </returns>
        /// <remarks>
        /// This ensures that URLs like https://open.spotify.com/track/ID?si=xyz are normalized to
        /// https://open.spotify.com/track/ID for consistent caching and comparison.
        /// </remarks>
        public static string NormalizeUrl( string? url ) {
            if (string.IsNullOrWhiteSpace( url )) {
                return string.Empty;
            }

            // Use the regex to extract the type and ID
            Match match = s_spotifyLink.Match( url );
            if (match.Success) {
                string type = match.Groups["type"].Value.ToLowerInvariant( );
                string id = match.Groups["id"].Value;
                return $"https://open.spotify.com/{type}/{id}";
            }

            // If we can't parse it, return the original URL
            return url;
        }

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

        /// <summary>
        /// Constructs an API URI for bulk track lookup by IDs.
        /// </summary>
        /// <param name="trackIds">The collection of Spotify track IDs (max 50).</param>
        /// <returns>The API URI for bulk track lookup.</returns>
        /// <remarks>
        /// Endpoint: GET /tracks?ids={comma-separated-ids}
        /// Maximum: 50 track IDs per request
        /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-several-tracks
        /// </remarks>
        public static string GetBulkTracksUri( IEnumerable<string> trackIds )
            => BulkTracksURI.Replace( "{ids}", string.Join( ",", trackIds ) );

        /// <summary>
        /// Constructs an API URI for bulk album lookup by IDs.
        /// </summary>
        /// <param name="albumIds">The collection of Spotify album IDs (max 20).</param>
        /// <returns>The API URI for bulk album lookup.</returns>
        /// <remarks>
        /// Endpoint: GET /albums?ids={comma-separated-ids}
        /// Maximum: 20 album IDs per request
        /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-multiple-albums
        /// </remarks>
        public static string GetBulkAlbumsUri( IEnumerable<string> albumIds )
            => BulkAlbumsURI.Replace( "{ids}", string.Join( ",", albumIds ) );

        /// <summary>
        /// Constructs an API URI for bulk artist lookup by IDs.
        /// </summary>
        /// <param name="artistIds">The collection of Spotify artist IDs (max 50).</param>
        /// <returns>The API URI for bulk artist lookup.</returns>
        /// <remarks>
        /// Endpoint: GET /artists?ids={comma-separated-ids}
        /// Maximum: 50 artist IDs per request
        /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-multiple-artists
        /// </remarks>
        public static string GetBulkArtistsUri( IEnumerable<string> artistIds )
            => BulkArtistsURI.Replace( "{ids}", string.Join( ",", artistIds ) );


        private const string TracksIsrcURI = "search?q=isrc:{isrc}&type=track";
        private const string AlbumsUpcURI = "search?q=upc:{upc}&type=album";
        private const string ArtistsSearchURI = "search?q={artist}&type=artist";
        private const string ArtistAlbumsURI = "artists/{id}/albums";
        private const string AlbumTracksURI = "albums/{id}/tracks";
        private const string AlbumsURI = "albums/{id}";
        private const string TracksURI = "tracks/{id}";
        private const string BulkTracksURI = "tracks?ids={ids}";
        private const string BulkAlbumsURI = "albums?ids={ids}";
        private const string BulkArtistsURI = "artists?ids={ids}";

    }
}
