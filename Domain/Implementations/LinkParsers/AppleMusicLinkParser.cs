using System.Text.RegularExpressions;
using TuneBridge.Domain.Implementations.Extensions;

namespace TuneBridge.Domain.Implementations.LinkParsers {

    /// <summary>
    /// Utility class for parsing Apple Music URLs and constructing Apple Music API request URIs.
    /// Handles both web player links (music.apple.com) and universal links, extracting storefronts,
    /// entity IDs, and entity types to build appropriate MusicKit API endpoints.
    /// </summary>
    /// <remarks>
    /// Apple Music URLs encode the storefront (market region) and entity type (song vs album) in their structure.
    /// This parser extracts those components and maps them to the corresponding MusicKit API endpoints.
    /// Supports both direct song links and album links with ?i= query parameters indicating specific tracks.
    /// </remarks>
    internal static partial class AppleMusicLinkParser {

        /// <summary>
        /// Parses an Apple Music web URL to extract the API request URI, market storefront, and content type.
        /// Handles both album and song URLs, including album URLs with track-specific query parameters.
        /// </summary>
        /// <param name="link">
        /// Apple Music URL in the format "https://music.apple.com/{storefront}/{type}/{id}".
        /// Supports both /album/ and /song/ paths, with or without storefront prefixes.
        /// </param>
        /// <param name="requestUri">
        /// Output: The constructed MusicKit API endpoint path for fetching entity details.
        /// </param>
        /// <param name="storefront">
        /// Output: The ISO 3166-1 alpha-2 country code extracted from the URL.
        /// Used for market-specific catalog queries.
        /// </param>
        /// <param name="isAlbum">
        /// Output: True if the URL points to an album, false if it points to a song/track.
        /// Determined by the path structure and presence of track-specific query parameters.
        /// </param>
        /// <returns>
        /// True if the URL was successfully parsed and recognized as a valid Apple Music URL.
        /// False if the URL doesn't match known Apple Music patterns or is malformed.
        /// </returns>
        /// <remarks>
        /// When an album URL includes "?i=songId", the parser prioritizes the song over the album.
        /// This matches user expectations when sharing specific tracks from album pages.
        /// </remarks>
        public static bool TryParseUri(
            string link,
            out string requestUri,
            out string storefront,
            out bool isAlbum
        ) {
            requestUri = string.Empty;
            storefront = string.Empty;
            isAlbum = false;
            if (s_appleLink.IsMatch( link )) {
                string? uri = s_appleLink.GetGroupValues(link, "URI").FirstOrDefault();

                if (uri != null && (s_validAlbum.IsMatch( uri ) || s_validSong.IsMatch( uri ))) {
                    string id = GetUriId(uri);
                    string albumSongId = GetSongId(uri);

                    storefront = GetUriStoreFront( uri );
                    if (string.IsNullOrWhiteSpace( albumSongId )) {
                        requestUri = GetAlbumsURI( storefront, id );
                        isAlbum = true;
                    } else {
                        requestUri = GetSongsURI( storefront, albumSongId );
                    }
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Constructs an API URI for searching artists by name.
        /// </summary>
        /// <param name="storefront">The market region/storefront.</param>
        /// <param name="artist">The artist name to search for.</param>
        /// <returns>The API URI for artist search.</returns>
        public static string GetArtistSearchUri( string storefront, string artist )
            => ArtistsSearchURI
                .Replace( "{storefront}", storefront )
                .Replace( "{artist}", Uri.EscapeDataString( artist ) );

        /// <summary>
        /// Constructs an API URI for looking up a song by ISRC.
        /// </summary>
        /// <param name="storefront">The market region/storefront.</param>
        /// <param name="isrc">The ISRC code.</param>
        /// <returns>The API URI for ISRC lookup.</returns>
        public static string GetSongsIsrcURI( string storefront, string isrc )
            => SongsIsrcURI
                .Replace( "{storefront}", storefront )
                .Replace( "{isrc}", isrc );

        /// <summary>
        /// Constructs an API URI for looking up an album by UPC.
        /// </summary>
        /// <param name="storefront">The market region/storefront.</param>
        /// <param name="upc">The UPC code.</param>
        /// <returns>The API URI for UPC lookup.</returns>
        public static string GetAlbumUpcURI( string storefront, string upc )
            => AlbumsUpcURI
                .Replace( "{storefront}", storefront )
                .Replace( "{upc}", upc );

        /// <summary>
        /// Constructs an API URI for getting an artist's albums.
        /// </summary>
        /// <param name="storefront">The market region/storefront.</param>
        /// <param name="artistId">The artist's ID.</param>
        /// <returns>The API URI for the artist's albums.</returns>
        public static string GetArtistAlbumsURI( string storefront, string artistId )
            => ArtistAlbumsURI
                .Replace( "{storefront}", storefront )
                .Replace( "{artist}", artistId );

        /// <summary>
        /// Constructs an API URI for getting an artist's songs.
        /// </summary>
        /// <param name="storefront">The market region/storefront.</param>
        /// <param name="artistId">The artist's ID.</param>
        /// <returns>The API URI for the artist's songs.</returns>
        public static string GetArtistSongsURI( string storefront, string artistId )
            => ArtistSongsURI
                .Replace( "{storefront}", storefront )
                .Replace( "{artist}", artistId );

        private const string AlbumsURI = "{storefront}/albums/{id}";
        private const string SongsURI = "{storefront}/songs/{id}";
        private const string SongsIsrcURI = "{storefront}/songs?filter[isrc]={isrc}";
        private const string AlbumsUpcURI = "{storefront}/albums?filter[upc]={upc}";
        private const string ArtistAlbumsURI = "{storefront}/artists/{artist}/albums";
        private const string ArtistSongsURI = "{storefront}/artists/{artist}/songs";
        private const string ArtistsSearchURI = "{storefront}/search?types=artists&term={artist}";
        private static string GetUriStoreFront( string uri )
            => uri.Split( '/' )[0];

        private static string GetUriId( string uri )
            => uri.Split( '/' ).Last( ).Split( '?' )[0];

        private static string GetAlbumsURI( string storefront, string id )
            => AlbumsURI.Replace( "{storefront}", storefront ).Replace( "{id}", id );

        private static string GetSongId( string uri )
            => s_albumSongIdRegex.IsMatch( uri )
                ? s_albumSongIdRegex.Match( uri ).Groups["songId"].Value
                : s_validSong.IsMatch( uri )
                    ? s_validSong.Match( uri ).Groups["Identifier"].Value
                    : string.Empty;

        private static string GetSongsURI( string storefront, string id )
            => SongsURI.Replace( "{storefront}", storefront ).Replace( "{id}", id );

        #region Regex

        private static readonly Regex s_appleLink = AppleMusicLink();
        [GeneratedRegex( @"[Mm][Uu][Ss][Ii][Cc]\.[Aa][Pp][Pp][Ll][Ee]\.[Cc][Oo][Mm]/(?<URI>[_\w\d\/\=\?\.\:\-%&]*)", RegexOptions.Compiled )]
        private static partial Regex AppleMusicLink( );

        private static readonly Regex s_albumSongIdRegex = AlbumSongId();
        [GeneratedRegex( @"\?i\=(?<songId>.*)", RegexOptions.Compiled )]
        private static partial Regex AlbumSongId( );

        private static readonly Regex s_validAlbum = ValidAlbumURI();
        [GeneratedRegex( @"(?<StoreFront>\w+)/[Aa][Ll][Bb][Uu][Mm]/(?<Identifier>.*)", RegexOptions.Compiled )]
        private static partial Regex ValidAlbumURI( );

        private static readonly Regex s_validSong = ValidSongURI();
        [GeneratedRegex( @"(?<StoreFront>\w+)/[Ss][Oo][Nn][Gg](?:/.*)?/(?<Identifier>.*)", RegexOptions.Compiled )]
        private static partial Regex ValidSongURI( );

        #endregion Regex

    }

}
