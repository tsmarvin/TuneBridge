using System.Text.RegularExpressions;
using BridgeBeats.Core.Domain.Providers.Common;
using BridgeBeats.Core.Domain.Utilities;

namespace BridgeBeats.Providers.AppleMusic {

    /// <summary>
    /// Parses Apple Music links and builds Apple Music API request URIs. Handles <c>music.apple.com</c>
    /// album and song URLs, extracting the storefront, entity identifier, and entity type.
    /// </summary>
    /// <remarks>
    /// The parser recognizes <c>music.apple.com</c> album and song URLs, extracts the storefront and entity
    /// identifier, and produces the relative API paths used by
    /// <see cref="BridgeBeats.Core.Domain.Providers.AppleMusic.AppleMusicLookupService"/>. It also
    /// exposes builders for the search, ISRC, UPC, and artist-catalog endpoints. An album URL carrying an
    /// <c>?i=</c> selector resolves to the individual song within the album. Matching is performed with
    /// compiled, source-generated regular expressions.
    /// </remarks>
    public static partial class AppleMusicLinkParser {

        /// <summary>
        /// Attempts to parse an Apple Music link into the API request URI for the referenced entity. Handles
        /// both album and song URLs, including album URLs with track-specific query parameters.
        /// </summary>
        /// <param name="link">The candidate Apple Music URL.</param>
        /// <param name="requestUri">
        /// When this method returns <see langword="true"/>, the relative Apple Music API path for the album
        /// or song; otherwise an empty string.
        /// </param>
        /// <param name="storefront">
        /// When this method returns <see langword="true"/>, the storefront (country) segment taken from the
        /// link; otherwise an empty string. This is an ISO 3166-1 alpha-2 country code used for
        /// market-specific catalog queries.
        /// </param>
        /// <param name="isAlbum">
        /// When this method returns <see langword="true"/>, indicates whether the link points to an album
        /// (<see langword="true"/>) or a song (<see langword="false"/>). A link carrying an <c>?i=</c>
        /// selector resolves to the individual song within the album.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the link is a recognized Apple Music album or song URL; otherwise
        /// <see langword="false"/>.
        /// </returns>
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
        /// Extracts the Apple Music entity identifier from a URL, delegating to the shared
        /// provider URL parser.
        /// </summary>
        /// <param name="url">The Apple Music URL to inspect.</param>
        /// <returns>The extracted identifier, or <see langword="null"/> if none is found.</returns>
        public static string? ExtractId( string url )
            => ProviderUrlParser.ExtractAppleMusicId( url );

        /// <summary>Builds the Apple Music API path that fetches a song by its catalog identifier.</summary>
        /// <param name="storefront">The storefront (country) segment.</param>
        /// <param name="songId">The Apple Music song identifier.</param>
        /// <returns>The relative API path for the song.</returns>
        public static string GetSongIdUri( string storefront, string songId )
            => GetSongsURI( storefront, songId );

        /// <summary>Builds the Apple Music API path that fetches an album by its catalog identifier.</summary>
        /// <param name="storefront">The storefront (country) segment.</param>
        /// <param name="albumId">The Apple Music album identifier.</param>
        /// <returns>The relative API path for the album.</returns>
        public static string GetAlbumIdUri( string storefront, string albumId )
            => GetAlbumsURI( storefront, albumId );

        /// <summary>Builds the Apple Music search API path that finds artists by name.</summary>
        /// <param name="storefront">The storefront (country) segment.</param>
        /// <param name="artist">The artist name; URL-escaped into the query.</param>
        /// <returns>The relative search API path filtered to artist results.</returns>
        public static string GetArtistSearchUri( string storefront, string artist )
            => ArtistsSearchURI
                .Replace( "{storefront}", storefront )
                .Replace( "{artist}", Uri.EscapeDataString( artist ) );

        /// <summary>Builds the Apple Music API path that finds songs by ISRC.</summary>
        /// <param name="storefront">The storefront (country) segment.</param>
        /// <param name="isrc">The International Standard Recording Code to filter on.</param>
        /// <returns>The relative API path filtered by ISRC.</returns>
        public static string GetSongsIsrcURI( string storefront, string isrc )
            => SongsIsrcURI
                .Replace( "{storefront}", storefront )
                .Replace( "{isrc}", isrc );

        /// <summary>Builds the Apple Music API path that finds albums by UPC.</summary>
        /// <param name="storefront">The storefront (country) segment.</param>
        /// <param name="upc">The Universal Product Code to filter on.</param>
        /// <returns>The relative API path filtered by UPC.</returns>
        public static string GetAlbumUpcURI( string storefront, string upc )
            => AlbumsUpcURI
                .Replace( "{storefront}", storefront )
                .Replace( "{upc}", upc );

        /// <summary>Builds the Apple Music API path that lists an artist's albums.</summary>
        /// <param name="storefront">The storefront (country) segment.</param>
        /// <param name="artistId">The Apple Music artist identifier.</param>
        /// <returns>The relative API path for the artist's albums.</returns>
        public static string GetArtistAlbumsURI( string storefront, string artistId )
            => ArtistAlbumsURI
                .Replace( "{storefront}", storefront )
                .Replace( "{artist}", artistId );

        /// <summary>Builds the Apple Music API path that lists an artist's songs.</summary>
        /// <param name="storefront">The storefront (country) segment.</param>
        /// <param name="artistId">The Apple Music artist identifier.</param>
        /// <returns>The relative API path for the artist's songs.</returns>
        public static string GetArtistSongsURI( string storefront, string artistId )
            => ArtistSongsURI
                .Replace( "{storefront}", storefront )
                .Replace( "{artist}", artistId );

        /// <summary>Template for the catalog album-by-id endpoint path.</summary>
        private const string AlbumsURI = "{storefront}/albums/{id}";

        /// <summary>Template for the catalog song-by-id endpoint path.</summary>
        private const string SongsURI = "{storefront}/songs/{id}";

        /// <summary>Template for the song search-by-ISRC endpoint path.</summary>
        private const string SongsIsrcURI = "{storefront}/songs?filter[isrc]={isrc}";

        /// <summary>Template for the album search-by-UPC endpoint path.</summary>
        private const string AlbumsUpcURI = "{storefront}/albums?filter[upc]={upc}";

        /// <summary>Template for the artist's-albums endpoint path.</summary>
        private const string ArtistAlbumsURI = "{storefront}/artists/{artist}/albums";

        /// <summary>Template for the artist's-songs endpoint path.</summary>
        private const string ArtistSongsURI = "{storefront}/artists/{artist}/songs";

        /// <summary>Template for the artist search endpoint path.</summary>
        private const string ArtistsSearchURI = "{storefront}/search?types=artists&term={artist}";

        /// <summary>Returns the storefront segment (the first path segment) of a parsed Apple Music URI.</summary>
        /// <param name="uri">The parsed Apple Music URI fragment.</param>
        /// <returns>The storefront (country) segment.</returns>
        private static string GetUriStoreFront( string uri )
            => uri.Split( '/' )[0];

        /// <summary>Returns the trailing identifier segment of a parsed Apple Music URI, dropping any query string.</summary>
        /// <param name="uri">The parsed Apple Music URI fragment.</param>
        /// <returns>The entity identifier.</returns>
        private static string GetUriId( string uri )
            => uri.Split( '/' ).Last( ).Split( '?' )[0];

        /// <summary>Fills the album-by-id template with the given storefront and album identifier.</summary>
        /// <param name="storefront">The storefront (country) segment.</param>
        /// <param name="id">The album identifier.</param>
        /// <returns>The relative API path for the album.</returns>
        private static string GetAlbumsURI( string storefront, string id )
            => AlbumsURI.Replace( "{storefront}", storefront ).Replace( "{id}", id );

        /// <summary>
        /// Extracts the song identifier from a parsed URI, preferring the <c>?i=</c> in-album selector and
        /// falling back to the trailing identifier of a song URL.
        /// </summary>
        /// <param name="uri">The parsed Apple Music URI fragment.</param>
        /// <returns>The song identifier, or an empty string if none is present.</returns>
        private static string GetSongId( string uri )
            => s_albumSongIdRegex.IsMatch( uri )
                ? s_albumSongIdRegex.Match( uri ).Groups["songId"].Value
                : s_validSong.IsMatch( uri )
                    ? s_validSong.Match( uri ).Groups["Identifier"].Value
                    : string.Empty;

        /// <summary>Fills the song-by-id template with the given storefront and song identifier.</summary>
        /// <param name="storefront">The storefront (country) segment.</param>
        /// <param name="id">The song identifier.</param>
        /// <returns>The relative API path for the song.</returns>
        private static string GetSongsURI( string storefront, string id )
            => SongsURI.Replace( "{storefront}", storefront ).Replace( "{id}", id );

        #region Regex

        /// <summary>Compiled regex matching a <c>music.apple.com</c> URL and capturing its path in the <c>URI</c> group.</summary>
        private static readonly Regex s_appleLink = AppleMusicLink();

        /// <summary>Source-generated factory for <see cref="s_appleLink"/>.</summary>
        /// <returns>The compiled Apple Music link regex.</returns>
        [GeneratedRegex( @"[Mm][Uu][Ss][Ii][Cc]\.[Aa][Pp][Pp][Ll][Ee]\.[Cc][Oo][Mm]/(?<URI>[_\w\d\/\=\?\.\:\-%&]*)", RegexOptions.Compiled )]
        private static partial Regex AppleMusicLink( );

        /// <summary>Compiled regex capturing the in-album song selector from a <c>?i=</c> query parameter.</summary>
        private static readonly Regex s_albumSongIdRegex = AlbumSongId();

        /// <summary>Source-generated factory for <see cref="s_albumSongIdRegex"/>.</summary>
        /// <returns>The compiled in-album song-id regex.</returns>
        [GeneratedRegex( @"\?i\=(?<songId>[^&#]*)", RegexOptions.Compiled )]
        private static partial Regex AlbumSongId( );

        /// <summary>Compiled regex validating an album URL and capturing its storefront and identifier.</summary>
        private static readonly Regex s_validAlbum = ValidAlbumURI();

        /// <summary>Source-generated factory for <see cref="s_validAlbum"/>.</summary>
        /// <returns>The compiled album-URL regex.</returns>
        [GeneratedRegex( @"(?<StoreFront>\w+)/[Aa][Ll][Bb][Uu][Mm]/(?<Identifier>[^?#]*)", RegexOptions.Compiled )]
        private static partial Regex ValidAlbumURI( );

        /// <summary>Compiled regex validating a song URL and capturing its storefront and identifier.</summary>
        private static readonly Regex s_validSong = ValidSongURI();

        /// <summary>Source-generated factory for <see cref="s_validSong"/>.</summary>
        /// <returns>The compiled song-URL regex.</returns>
        [GeneratedRegex( @"(?<StoreFront>\w+)/[Ss][Oo][Nn][Gg](?:/.*)?/(?<Identifier>[^?#]*)", RegexOptions.Compiled )]
        private static partial Regex ValidSongURI( );

        #endregion Regex

    }

}
