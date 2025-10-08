using System.Text.RegularExpressions;
using TuneBridge.Domain.Implementations.Extensions;

namespace TuneBridge.Domain.Implementations.LinkParsers {

    internal static partial class AppleMusicLinkParser {

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

        public static string GetArtistSearchUri( string storefront, string artist )
            => ArtistsSearchURI
                .Replace( "{storefront}", storefront )
                .Replace( "{artist}", Uri.EscapeDataString( artist ) );

        public static string GetSongsIsrcURI( string storefront, string isrc )
            => SongsIsrcURI
                .Replace( "{storefront}", storefront )
                .Replace( "{isrc}", isrc );

        public static string GetAlbumUpcURI( string storefront, string upc )
            => AlbumsUpcURI
                .Replace( "{storefront}", storefront )
                .Replace( "{upc}", upc );

        public static string GetArtistAlbumsURI( string storefront, string artistId )
            => ArtistAlbumsURI
                .Replace( "{storefront}", storefront )
                .Replace( "{artist}", artistId );

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
