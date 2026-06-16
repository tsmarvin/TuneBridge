using System.Text.RegularExpressions;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Utilities;
using idunno.Security;

namespace BridgeBeats.Providers.Spotify {

    /// <summary>
    /// Utility class for parsing Spotify URLs and constructing Spotify Web API request URIs.
    /// Recognizes track, album, and prerelease entities from open.spotify.com URLs,
    /// then maps them to the corresponding API v1 endpoints for metadata retrieval.
    /// </summary>
    /// <remarks>
    /// The parser recognizes open.spotify.com/{type}/{id} URLs where {type} is "track",
    /// "album", or "prerelease", and {id} is a Base62-encoded identifier. Artist and
    /// playlist URLs are not recognized by the regex and are dropped by design;
    /// neither entity type is processed anywhere in the system.
    /// Short <c>spotify.link</c> URLs are resolved to their canonical <c>open.spotify.com</c> form by
    /// following a single redirect through an SSRF-hardened HTTP handler. The URL-matching regexes here
    /// overlap with the provider-agnostic <see cref="ProviderUrlParser"/>; this parser is the
    /// Spotify-specific one.
    /// </remarks>
    public static partial class SpotifyLinkParser {

        /// <summary>
        /// Parses a Spotify web URL to extract the entity type and Spotify ID. Validates URL structure
        /// and determines whether the link points to a track, album, or prerelease.
        /// </summary>
        /// <param name="link">
        /// Spotify URL in the format "https://open.spotify.com/{type}/{id}" or "https://spotify.link/{code}".
        /// Must follow the open.spotify.com or spotify.link pattern. Query parameters are ignored.
        /// </param>
        /// <returns>
        /// A tuple containing: (bool success, SpotifyEntity kind, string id).
        /// - success: True if the URL was successfully parsed and recognized as track, album, or prerelease.
        /// - kind: The entity type extracted from the URL.
        /// - id: The Spotify ID (Base62 alphanumeric string, typically 22 characters).
        /// </returns>
        /// <remarks>
        /// Recognized entity types are track, album, and prerelease. Artist and playlist URLs
        /// are not matched by the regex and are dropped by design — neither entity type is
        /// processed anywhere in the system.
        /// For spotify.link URLs, the method follows the redirect to obtain the actual open.spotify.com URL.
        /// A non-<c>spotify.link</c> host that matched the short-link shape returns a failed result.
        /// </remarks>
        public static async Task<(bool, SpotifyEntity kind, string id)> TryParseUriAsync(
            string link
        ) {
            SpotifyEntity kind = SpotifyEntity.Unknown;
            string id = string.Empty;

            // If it's a spotify.link URL, resolve it to the actual Spotify URL.
            // The host must be exactly "spotify.link" before any outbound fetch is attempted.
            if (s_spotifyShortLink.IsMatch( link )) {
                // Strip any existing http(s):// prefix so both the host-gate URI construction
                // and the resolve fetch work correctly for bare and scheme-prefixed inputs.
                // Without this, "https://spotify.link/x" would produce "https://https://…" and
                // Uri.Host would equal "https" instead of "spotify.link", silently dropping
                // a legitimate short link.
                string bareLink = link.StartsWith( "https://", StringComparison.OrdinalIgnoreCase )
                    ? link[ "https://".Length.. ]
                    : link.StartsWith( "http://", StringComparison.OrdinalIgnoreCase )
                        ? link[ "http://".Length.. ]
                        : link;
                Uri parsed = new( $"https://{bareLink}" );
                if (parsed.Host.Equals( "spotify.link", StringComparison.OrdinalIgnoreCase )) {
                    string? resolvedUrl = await ResolveSpotifyShortLinkAsync( bareLink );
                    if (resolvedUrl != null) {
                        link = resolvedUrl;
                    }
                } else {
                    return (false, kind, id);
                }
            }

            if (s_spotifyLink.IsMatch( link )) {
                Match match = s_spotifyLink.Match(link);

                id = match.Groups["id"].Value;
                kind = match.Groups["type"].Value.ToLowerInvariant( ) switch {
                    "track" => SpotifyEntity.Track,
                    "album" => SpotifyEntity.Album,
                    "prerelease" => SpotifyEntity.PreRelease,
                    _ => SpotifyEntity.Unknown
                };
            }

            return (kind != SpotifyEntity.Unknown && !string.IsNullOrEmpty( id ), kind, id);
        }

        /// <summary>
        /// Resolves a spotify.link short URL to the actual open.spotify.com URL by following the HTTP
        /// redirect through an SSRF-hardened handler.
        /// </summary>
        /// <param name="shortLink">The spotify.link URL (without scheme) to resolve.</param>
        /// <returns>The resolved open.spotify.com URL, or null if resolution fails or the response is not a redirect.</returns>
        internal static async Task<string?> ResolveSpotifyShortLinkAsync( string shortLink ) {
            using HttpClient client = new( s_handlerFactory( ) );
            client.Timeout = TimeSpan.FromSeconds( 5 );
            HttpResponseMessage response = await client.GetAsync( $"https://{shortLink}" );
            return response.StatusCode is System.Net.HttpStatusCode.MovedPermanently or
                   System.Net.HttpStatusCode.Found or
                   System.Net.HttpStatusCode.SeeOther or
                   System.Net.HttpStatusCode.TemporaryRedirect
                ? (response.Headers.Location?.ToString( ))
                : null;
        }

        /// <summary>
        /// Factory for the SSRF-hardened HTTP handler used when resolving short links; auto-redirect is
        /// disabled so the redirect target can be read from the response. Replaceable via
        /// <see cref="SetHandlerFactoryForTests"/>.
        /// </summary>
        private static Func<HttpMessageHandler> s_handlerFactory = ( ) =>
            SsrfSocketsHttpHandlerFactory.Create(
                connectTimeout: TimeSpan.FromSeconds( 5 ),
                allowAutoRedirect: false );

        /// <summary>Test seam that overrides the short-link resolution handler factory.</summary>
        /// <param name="factory">The handler factory to use in place of the SSRF-hardened default.</param>
        internal static void SetHandlerFactoryForTests( Func<HttpMessageHandler> factory ) =>
            s_handlerFactory = factory;

        /// <summary>Compiled regex matching a full <c>open.spotify.com</c> track, album, or prerelease link.</summary>
        private static readonly Regex s_spotifyLink = SpotifyMusicLink();

        /// <summary>Builds the compiled <see cref="s_spotifyLink"/> regex.</summary>
        /// <returns>The compiled regex.</returns>
        [GeneratedRegex( @"(?:open\.spotify\.com/)(?<type>track|album|prerelease)/(?<id>[A-Za-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex SpotifyMusicLink( );

        /// <summary>Compiled regex matching a <c>spotify.link</c> short link.</summary>
        private static readonly Regex s_spotifyShortLink = SpotifyShortLinkPattern();

        /// <summary>Builds the compiled <see cref="s_spotifyShortLink"/> regex.</summary>
        /// <returns>The compiled regex.</returns>
        [GeneratedRegex( @"^(?:https?://)?spotify\.link/[A-Za-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex SpotifyShortLinkPattern( );

        /// <summary>
        /// Extracts the Spotify ID (track or album) from a URL.
        /// </summary>
        /// <param name="url">The Spotify URL to parse.</param>
        /// <returns>The extracted ID, or null if the URL is invalid or cannot be parsed.</returns>
        /// <remarks>
        /// This is a synchronous method that only handles direct open.spotify.com URLs.
        /// It does not resolve spotify.link short URLs. For full URL resolution including
        /// short links, use <see cref="TryParseUriAsync"/>. Delegates to <see cref="ProviderUrlParser.ExtractSpotifyId"/>.
        /// </remarks>
        public static string? ExtractId( string url )
            => ProviderUrlParser.ExtractSpotifyId( url );

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

        /// <summary>Template for the ISRC track-search request URI.</summary>
        private const string TracksIsrcURI = "search?q=isrc:{isrc}&type=track";

        /// <summary>Template for the UPC album-search request URI.</summary>
        private const string AlbumsUpcURI = "search?q=upc:{upc}&type=album";

        /// <summary>Template for the artist-search request URI.</summary>
        private const string ArtistsSearchURI = "search?q={artist}&type=artist";

        /// <summary>Template for the artist-albums request URI.</summary>
        private const string ArtistAlbumsURI = "artists/{id}/albums";

        /// <summary>Template for the album-tracks request URI.</summary>
        private const string AlbumTracksURI = "albums/{id}/tracks";

        /// <summary>Template for the single-album request URI.</summary>
        private const string AlbumsURI = "albums/{id}";

        /// <summary>Template for the single-track request URI.</summary>
        private const string TracksURI = "tracks/{id}";

        /// <summary>Template for the bulk-tracks request URI.</summary>
        private const string BulkTracksURI = "tracks?ids={ids}";

        /// <summary>Template for the bulk-albums request URI.</summary>
        private const string BulkAlbumsURI = "albums?ids={ids}";

        /// <summary>Template for the bulk-artists request URI.</summary>
        private const string BulkArtistsURI = "artists?ids={ids}";

    }
}
